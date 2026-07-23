using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.ModelBinding;

namespace Laraue.Apps.Boards.WebApiServices;

/// <summary>
/// Binds a form field by deserializing its raw string value as JSON, using System.Text.Json.
/// Unlike the default complex-object form binder, this respects [JsonDerivedType]/[JsonPolymorphic]
/// attributes, so it can construct the correct derived type (e.g. StringAttributeValue vs
/// EnumAttributeValue) instead of failing on the abstract base type.
/// Apply via [ModelBinder(typeof(JsonModelBinder))] or the [JsonModelBinder] attribute below
/// on any [FromForm] property that needs polymorphic or otherwise JSON-only binding.
/// </summary>
public class JsonModelBinder : IModelBinder
{
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);
 
    public Task BindModelAsync(ModelBindingContext bindingContext)
    {
        ArgumentNullException.ThrowIfNull(bindingContext);
 
        var valueProviderResult = bindingContext.ValueProvider.GetValue(bindingContext.ModelName);
        if (valueProviderResult == ValueProviderResult.None)
            return Task.CompletedTask;
 
        bindingContext.ModelState.SetModelValue(bindingContext.ModelName, valueProviderResult);
 
        var rawValue = valueProviderResult.FirstValue;
        if (string.IsNullOrEmpty(rawValue))
            return Task.CompletedTask;
 
        try
        {
            var model = JsonSerializer.Deserialize(rawValue, bindingContext.ModelType, Options);
            bindingContext.Result = ModelBindingResult.Success(model);
        }
        catch (JsonException ex)
        {
            bindingContext.ModelState.TryAddModelError(
                bindingContext.ModelName,
                $"Invalid JSON value for '{bindingContext.ModelName}': {ex.Message}");
        }
 
        return Task.CompletedTask;
    }
}
 
/// <summary>
/// Shorthand for [ModelBinder(typeof(JsonModelBinder))].
/// </summary>
public class JsonModelBinderAttribute : ModelBinderAttribute
{
    public JsonModelBinderAttribute()
    {
        BinderType = typeof(JsonModelBinder);
    }
}
 