namespace WheelHouse.Core.Models;

/// <summary>
/// A reusable, parameterized prompt for the Gemini R&amp;D workflow. The <see cref="Body"/>
/// contains <c>{{PLACEHOLDER}}</c> tokens that are filled in before sending to Gemini.
/// </summary>
public class PromptTemplate
{
    public int Id { get; set; }

    public string Name { get; set; } = string.Empty;

    /// <summary>Grouping shown in the library, e.g. "Research", "Debugging", "Database".</summary>
    public string Category { get; set; } = "General";

    /// <summary>One-line description of when to use this template.</summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>The prompt text, including <c>{{PLACEHOLDER}}</c> tokens.</summary>
    public string Body { get; set; } = string.Empty;

    /// <summary>True for templates shipped with WheelHouse (not user-created).</summary>
    public bool IsBuiltIn { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }
}
