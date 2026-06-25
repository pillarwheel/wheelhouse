using WheelHouse.Core.Models;

namespace WheelHouse.Core.Interfaces;

/// <summary>Manages the prompt-template library and renders templates with supplied values.</summary>
public interface IPromptTemplateService
{
    /// <summary>Returns the distinct <c>{{PLACEHOLDER}}</c> names in a body, in first-seen order.</summary>
    IReadOnlyList<string> ExtractPlaceholders(string body);

    /// <summary>
    /// Substitutes <c>{{PLACEHOLDER}}</c> tokens with the supplied values. Tokens without a
    /// (non-empty) value are left intact so the gaps remain visible.
    /// </summary>
    string Render(string body, IReadOnlyDictionary<string, string?> values);

    Task<IReadOnlyList<PromptTemplate>> GetAllAsync(CancellationToken cancellationToken = default);
    Task<PromptTemplate?> GetAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Creates or updates a (custom) template.</summary>
    Task<PromptTemplate> SaveAsync(PromptTemplate template, CancellationToken cancellationToken = default);

    Task DeleteAsync(int id, CancellationToken cancellationToken = default);

    /// <summary>Inserts any built-in templates that aren't already present. Idempotent.</summary>
    Task<int> SeedBuiltInsAsync(CancellationToken cancellationToken = default);
}
