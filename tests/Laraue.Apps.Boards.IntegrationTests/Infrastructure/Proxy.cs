using System.Linq.Expressions;
using System.Net;
using System.Net.Http.Json;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using Laraue.Apps.Boards.WebApiServices;
using Laraue.Core.Exceptions;
using Laraue.Core.Exceptions.Web;
using Laraue.Telegram.NET.Abstractions.Request;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Routing;
using Microsoft.Extensions.DependencyInjection;
using FromQueryAttribute = Microsoft.AspNetCore.Mvc.FromQueryAttribute;

namespace Laraue.Apps.Boards.IntegrationTests.Infrastructure;

public class Proxy<TController>(HttpClient client, WebApiTestHost host) where TController : ControllerBase
{
    private readonly JsonSerializerOptions _options = new(JsonSerializerDefaults.Web);
    
    public async Task<T?> Execute<T>(Expression<Func<TController, Task<T>>> makeCall)
    {
        var nonGenericCall = ConvertToNonGeneric(makeCall);
        var response = await ExecuteInternal(nonGenericCall);
        if (typeof(T) == typeof(string))
            return (dynamic) await response.Content.ReadAsStringAsync();
        return await response.Content.ReadFromJsonAsync<T>();
    }
    
    public Task Execute(Expression<Func<TController, Task>> makeCall)
    {
        return ExecuteInternal(makeCall);
    }

    public Proxy<TController> WithUserAuthorization(Guid userId)
    {
        var authService = host.Services.GetRequiredService<IAuthService>();
        var bearer = authService.CreateUserToken(userId);
        return WithAuthorizationToken(bearer);
    }

    public Proxy<TController> WithOrganizationAuthorization(long organizationId, Guid userId)
    {
        var authService = host.Services.GetRequiredService<IAuthService>();
        var bearer = authService.CreateOrganizationToken(organizationId, userId);
        return WithAuthorizationToken(bearer);
    }

    public Proxy<TController> WithAuthorizationToken(string token)
    {
        const string headerName = "Authorization";
        client.DefaultRequestHeaders.Remove(headerName);
        client.DefaultRequestHeaders.Add(headerName, $"Bearer {token}");
        return this;
    }
    
    private async Task<HttpResponseMessage> ExecuteInternal(Expression<Func<TController, Task>> makeCall)
    {
        var controller = typeof(TController);
        var routeAttribute = controller.GetCustomAttribute<RouteAttribute>()
            ?? throw new InvalidOperationException($"Route attribute on {controller} excepted");
        var controllerPath = routeAttribute.Template;

        var call = makeCall.Body;
        if (call is UnaryExpression unary)
            call = unary.Operand;

        if (call is not MethodCallExpression methodExpr)
            throw new InvalidOperationException($"Method call {call} excepted");
        
        var method = methodExpr.Method;
        var httpAttribute = method.GetCustomAttribute<HttpMethodAttribute>(true);
        if (httpAttribute == null)
            throw new InvalidOperationException($"Method {method} should be marked as HTTP attribute, e.g. [HttpGet] to be called.");
        
        var templateParameters = GetTemplateParameters(httpAttribute.Template);

        var methodArguments = method
            .GetParameters()
            .Zip(methodExpr.Arguments)
            .Select(x =>
            {
                BindType? bindType = null;
                
                if (templateParameters.Select(y => y.Name).Contains(x.First.Name))
                    bindType = BindType.FromPath;
                
                if (bindType is null && x.First.GetCustomAttribute<FromQueryAttribute>() != null)
                    bindType = BindType.FromQuery;
                
                if (bindType is null && x.First.GetCustomAttribute<FromPathAttribute>() != null)
                    bindType = BindType.FromPath;
                
                if (bindType is null && x.First.GetCustomAttribute<FromBodyAttribute>() != null)
                    bindType = BindType.FromBody;

                return new
                {
                    BindType = bindType,
                    Name = x.First.Name ?? string.Empty,
                    Expression = x.Second,
                };
            });
        
        var query = new Dictionary<string, object?>();
        var body = new Dictionary<string, object?>();
        var path = new Dictionary<string, object?>();
        
        foreach (var arg in methodArguments)
        {
            var values = arg.BindType switch
            {
                BindType.FromBody => body,
                BindType.FromPath => path,
                BindType.FromQuery => query,
                _ => null
            };
            
            if (values is null)
                continue;

            switch (arg.Expression)
            {
                case MemberInitExpression initExpr:
                {
                    foreach (var binding in initExpr.Bindings)
                    {
                        var assignment = (MemberAssignment)binding;
                        var value = Expression.Lambda(assignment.Expression).Compile().DynamicInvoke()!;
                        values[assignment.Member.Name] = value;
                    }

                    break;
                }
                case ConstantExpression constExpr:
                    values[arg.Name] = constExpr.Value;
                    break;
                case MemberExpression memberExpr:
                    var memberValue = Expression.Lambda(memberExpr).Compile().DynamicInvoke();
                    var memberType = memberValue!.GetType();
                    if (memberType.IsClass && memberType != typeof(string))
                    {
                        var properties = memberType.GetProperties(BindingFlags.Instance | BindingFlags.Public);
                        foreach (var property in properties)
                            values[property.Name] = property.GetValue(memberValue);
                    }
                    else
                        values[arg.Name] = memberValue;
                    break;
                default:
                    values[arg.Name] = Expression.Lambda(arg.Expression).Compile().DynamicInvoke();
                    break;
            }
        }

        var fullPath = controllerPath + (httpAttribute.Template is not null ? $"/{httpAttribute.Template}" : string.Empty);
        if (query.Any())
            fullPath += "?" + string.Join("&", query.Select(x => $"{x.Key}={x.Value}"));

        foreach (var pathParameter in path)
        {
            var templateParameter = templateParameters.First(x => x.Name == pathParameter.Key);
            var pattern = templateParameter.RoutePattern;
            fullPath = fullPath.Replace(pattern, pathParameter.Value!.ToString());
        }

        var bodyString = JsonSerializer.Serialize(body, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        var stringContent = new StringContent(bodyString, Encoding.UTF8, "application/json");
        
        var responseTask = httpAttribute switch
        {
            HttpGetAttribute => client.GetAsync(fullPath),
            HttpPostAttribute => client.PostAsync(fullPath, stringContent),
            HttpPutAttribute => client.PutAsync(fullPath, stringContent),
            HttpDeleteAttribute => client.DeleteAsync(fullPath),
            _ => throw new InvalidOperationException($"Requests with {httpAttribute} are not supported")
        };
        
        var response = await responseTask;
        await HandleNonSuccessCode(response, bodyString);
        return response;
    }

    private static TemplateParameter[] GetTemplateParameters(string? template)
    {
        if (template == null)
            return [];
        
        var regex = new Regex("{(\\w+)(?::(\\w+))?}", RegexOptions.Compiled);
        var matches = regex.Matches(template);
        return matches
            .Select(x => new TemplateParameter
            {
                Name = x.Groups[1].Value,
                RoutePattern = x.Groups[0].Value,
            })
            .ToArray();
    }

    private record TemplateParameter
    {
        public required string Name { get; set; }
        public required string RoutePattern { get; set; }
    }

    private static Expression<Func<TController, Task>> ConvertToNonGeneric<T>(
        Expression<Func<TController, Task<T>>> expression)
    {
        var parameter = expression.Parameters[0];
        var convertedBody = Expression.Convert(expression.Body, typeof(Task));
        return Expression.Lambda<Func<TController, Task>>(convertedBody, parameter);
    }

    private async Task HandleNonSuccessCode(HttpResponseMessage response, string bodyString)
    {
        if (!response.IsSuccessStatusCode)
        {
            var responseContent = await response.Content.ReadAsStringAsync();
            var error =
                $"[{response.RequestMessage?.Method}] {response.RequestMessage?.RequestUri} ({response.StatusCode:D}) \nRequest Content: {bodyString}\nResponse Content:{responseContent}";

            var errorResponse = JsonSerializer.Deserialize<ErrorResponse>(responseContent, new JsonSerializerOptions(JsonSerializerDefaults.Web))!;
            Exception? inner = response.StatusCode switch
            {
                HttpStatusCode.BadRequest => new BadRequestException(errorResponse.Errors!),
                HttpStatusCode.NotFound => new NotFoundException(errorResponse.Message),
                HttpStatusCode.Forbidden => new ForbiddenException(errorResponse.Message),
                _ => null
            };
            
            throw new HttpRequestException(error, inner, response.StatusCode);
        }
    }

    private enum BindType
    {
        FromQuery,
        FromPath,
        FromBody,
    }
}