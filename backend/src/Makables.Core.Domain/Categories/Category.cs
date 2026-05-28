using System.Globalization;
using System.Text;
using Makables.Core.Domain.Common;

namespace Makables.Core.Domain.Categories;

/// <summary>
/// A product category per ADR 0004 (per-country reference data) +
/// US-admin-0013. Seeded with the six launch categories
/// ("3D tisk", "Klasický tisk", "Potisk textilu", "Laser &amp; CNC",
/// "Velkoformát", "Handmade") and admin-managed thereafter.
///
/// <para>
/// The <see cref="Slug"/> is the URL segment under <c>/katalog/?kategorie={slug}</c>
/// and is derived from <see cref="Name"/> at construction time
/// (diacritics stripped, non-alphanumerics replaced with <c>-</c>,
/// runs collapsed). Renames keep the original slug per US-admin-0013
/// AC-2 — invariants need to survive name churn ("existing products
/// keep their FK"; if a public URL changes silently on rename, every
/// SEO link breaks).
/// </para>
///
/// <para>
/// Auditable so <see cref="Common.Auditable.IsActive"/> drives the
/// "hidden from new-product forms" behaviour without losing the row.
/// </para>
/// </summary>
public sealed class Category : Auditable
{
    private const int MaxNameLength = 100;
    private const int MaxSlugLength = 100;
    private const int MaxIconLength = 64;
    private const int MaxDescriptionLength = 500;

    public string Name { get; private set; } = default!;
    public string Slug { get; private set; } = default!;
    public string? Icon { get; private set; }
    public string? Description { get; private set; }
    public int SortOrder { get; private set; }

    private Category() { }

    public static Category Create(
        string id,
        string name,
        string? slug,
        string? icon,
        string? description,
        int sortOrder,
        string countryCode)
    {
        if (string.IsNullOrWhiteSpace(id))
            throw new ArgumentException("Id is required.", nameof(id));
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
            throw new ArgumentException($"Name must be at most {MaxNameLength} chars (post-trim).", nameof(name));

        // Slug: caller may supply one (admin override); otherwise derive
        // from Name. The derived slug is stable across renames because
        // the admin command does NOT touch Slug on update (US-admin-0013
        // AC-2 — products keep their FK to the existing row, but the
        // URL segment should also stay stable so SEO links don't break).
        var resolvedSlug = string.IsNullOrWhiteSpace(slug) ? Slugify(trimmedName) : slug.Trim();
        if (resolvedSlug.Length == 0)
            throw new ArgumentException("Slug derivation produced an empty string. Provide an explicit slug.", nameof(slug));
        if (resolvedSlug.Length > MaxSlugLength)
            throw new ArgumentException($"Slug must be at most {MaxSlugLength} chars.", nameof(slug));
        if (!IsValidSlug(resolvedSlug))
            throw new ArgumentException("Slug must match [a-z0-9-]+ (no leading/trailing/double dashes).", nameof(slug));

        if (string.IsNullOrWhiteSpace(countryCode) || countryCode.Length != 2)
            throw new ArgumentException("CountryCode must be 2 chars (ISO 3166-1 alpha-2).", nameof(countryCode));

        var trimmedIcon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        if (trimmedIcon is not null && trimmedIcon.Length > MaxIconLength)
            throw new ArgumentException($"Icon must be at most {MaxIconLength} chars.", nameof(icon));

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is not null && trimmedDescription.Length > MaxDescriptionLength)
            throw new ArgumentException($"Description must be at most {MaxDescriptionLength} chars.", nameof(description));

        return new Category
        {
            Id = id,
            Name = trimmedName,
            Slug = resolvedSlug,
            Icon = trimmedIcon,
            Description = trimmedDescription,
            SortOrder = sortOrder,
            CountryCode = countryCode.ToUpperInvariant(),
        };
    }

    /// <summary>
    /// Admin rename + metadata patch. Per US-admin-0013 AC-2, <see cref="Slug"/>
    /// is NOT touched here — renaming "Velkoformát" to "Velký formát" keeps
    /// the existing URL segment intact so external links don't break and
    /// product FKs remain valid by primary key (which is unrelated to slug
    /// either way, but slug stability is the documented promise).
    /// </summary>
    public Category UpdateMetadata(string name, string? icon, string? description, int sortOrder)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("Name is required.", nameof(name));
        var trimmedName = name.Trim();
        if (trimmedName.Length > MaxNameLength)
            throw new ArgumentException($"Name must be at most {MaxNameLength} chars.", nameof(name));

        var trimmedIcon = string.IsNullOrWhiteSpace(icon) ? null : icon.Trim();
        if (trimmedIcon is not null && trimmedIcon.Length > MaxIconLength)
            throw new ArgumentException($"Icon must be at most {MaxIconLength} chars.", nameof(icon));

        var trimmedDescription = string.IsNullOrWhiteSpace(description) ? null : description.Trim();
        if (trimmedDescription is not null && trimmedDescription.Length > MaxDescriptionLength)
            throw new ArgumentException($"Description must be at most {MaxDescriptionLength} chars.", nameof(description));

        Name = trimmedName;
        Icon = trimmedIcon;
        Description = trimmedDescription;
        SortOrder = sortOrder;
        return this;
    }

    /// <summary>
    /// Generate a Czech-friendly URL slug. Steps:
    /// <list type="number">
    ///   <item><description>Unicode NFD-decompose so combining diacritics separate from their base letters.</description></item>
    ///   <item><description>Drop combining marks (this strips čďěíňřšťůýž → cdeinrstuyz).</description></item>
    ///   <item><description>Lowercase invariant.</description></item>
    ///   <item><description>Replace runs of non-<c>[a-z0-9]</c> with a single dash.</description></item>
    ///   <item><description>Trim leading/trailing dashes.</description></item>
    /// </list>
    /// </summary>
    public static string Slugify(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var normalized = value.Trim().Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(normalized.Length);
        var prevDash = true;  // suppresses a leading dash

        foreach (var c in normalized)
        {
            var category = CharUnicodeInfo.GetUnicodeCategory(c);
            if (category == UnicodeCategory.NonSpacingMark) continue;

            var lower = char.ToLowerInvariant(c);
            if ((lower >= 'a' && lower <= 'z') || (lower >= '0' && lower <= '9'))
            {
                sb.Append(lower);
                prevDash = false;
            }
            else if (!prevDash)
            {
                sb.Append('-');
                prevDash = true;
            }
        }

        var result = sb.ToString();
        return result.EndsWith('-') ? result[..^1] : result;
    }

    private static bool IsValidSlug(string slug)
    {
        if (slug.Length == 0) return false;
        if (slug[0] == '-' || slug[^1] == '-') return false;
        var prevDash = false;
        foreach (var c in slug)
        {
            var isAlphaNum = (c >= 'a' && c <= 'z') || (c >= '0' && c <= '9');
            if (isAlphaNum)
            {
                prevDash = false;
                continue;
            }
            if (c == '-')
            {
                if (prevDash) return false;  // no double dash
                prevDash = true;
                continue;
            }
            return false;
        }
        return true;
    }
}
