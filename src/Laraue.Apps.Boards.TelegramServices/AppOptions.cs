using System.ComponentModel.DataAnnotations;

namespace Laraue.Apps.Boards.TelegramServices;

public class AppOptions : IValidatableObject
{
    [Required]
    [Url]
    public required string Url { get; set; }

    [Required]
    public required IconsUrls Icons { get; set; }

    /// <summary>
    /// <c>ValidateDataAnnotations</c> doesn't descend into nested objects on its own - validate
    /// <see cref="Icons"/>' own attributes here so a missing icon URL is caught too.
    /// </summary>
    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(Icons, new ValidationContext(Icons), results, validateAllProperties: true);

        return results.Select(r => new ValidationResult(
            $"{nameof(Icons)}: {r.ErrorMessage}",
            r.MemberNames.Select(m => $"{nameof(Icons)}.{m}")));
    }
}

public class IconsUrls
{
    [Required]
    [Url]
    public required string Issue { get; set; }

    [Required]
    [Url]
    public required string Organization { get; set; }

    [Required]
    [Url]
    public required string User { get; set; }

    [Required]
    [Url]
    public required string Hint { get; set; }

    [Required]
    [Url]
    public required string Space { get; set; }
}