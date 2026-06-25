using Microsoft.EntityFrameworkCore;
using WheelHouse.Core.Interfaces;
using WheelHouse.Core.Models;
using WheelHouse.Core.Prompts;
using WheelHouse.Infrastructure.Persistence;

namespace WheelHouse.Infrastructure.Prompts;

/// <summary>
/// DB-backed prompt-template library. Placeholder extraction / rendering is delegated to the
/// pure <see cref="PromptRendering"/> helper so the same logic powers the live UI.
/// </summary>
public class PromptTemplateService : IPromptTemplateService
{
    private readonly WheelHouseDbContext _db;

    public PromptTemplateService(WheelHouseDbContext db) => _db = db;

    public IReadOnlyList<string> ExtractPlaceholders(string body)
        => PromptRendering.ExtractPlaceholders(body);

    public string Render(string body, IReadOnlyDictionary<string, string?> values)
        => PromptRendering.Render(body, values);

    public async Task<IReadOnlyList<PromptTemplate>> GetAllAsync(CancellationToken cancellationToken = default)
        => await _db.PromptTemplates
            .OrderByDescending(t => t.IsBuiltIn)
            .ThenBy(t => t.Category)
            .ThenBy(t => t.Name)
            .ToListAsync(cancellationToken);

    public Task<PromptTemplate?> GetAsync(int id, CancellationToken cancellationToken = default)
        => _db.PromptTemplates.FirstOrDefaultAsync(t => t.Id == id, cancellationToken);

    public async Task<PromptTemplate> SaveAsync(PromptTemplate template, CancellationToken cancellationToken = default)
    {
        if (template.Id == 0)
        {
            template.CreatedAt = DateTime.UtcNow;
            _db.PromptTemplates.Add(template);
        }
        else
        {
            template.UpdatedAt = DateTime.UtcNow;
            _db.PromptTemplates.Update(template);
        }
        await _db.SaveChangesAsync(cancellationToken);
        return template;
    }

    public async Task DeleteAsync(int id, CancellationToken cancellationToken = default)
    {
        var t = await _db.PromptTemplates.FindAsync(new object?[] { id }, cancellationToken);
        if (t is null) return;
        _db.PromptTemplates.Remove(t);
        await _db.SaveChangesAsync(cancellationToken);
    }

    public async Task<int> SeedBuiltInsAsync(CancellationToken cancellationToken = default)
    {
        var existing = await _db.PromptTemplates
            .Where(t => t.IsBuiltIn)
            .Select(t => t.Name)
            .ToListAsync(cancellationToken);
        var existingSet = existing.ToHashSet(StringComparer.Ordinal);

        var added = 0;
        foreach (var builtIn in BuiltInPromptTemplates.All)
        {
            if (existingSet.Contains(builtIn.Name)) continue;
            _db.PromptTemplates.Add(new PromptTemplate
            {
                Name = builtIn.Name,
                Category = builtIn.Category,
                Description = builtIn.Description,
                Body = builtIn.Body,
                IsBuiltIn = true,
                CreatedAt = DateTime.UtcNow
            });
            added++;
        }

        if (added > 0) await _db.SaveChangesAsync(cancellationToken);
        return added;
    }
}
