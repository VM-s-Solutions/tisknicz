/**
 * Canonicalizes a JSON document for hashing: recursively sorts object
 * keys (ordinal) so semantically-identical specs hash identically
 * regardless of controller/action discovery order.
 *
 * Why this exists: ASP.NET Core's OpenAPI document writer emits
 * `paths`/`components.schemas` in controller-discovery order, which is
 * not part of the OpenAPI contract and can differ between build
 * environments (e.g. a local macOS build vs. the CI Linux runner) even
 * when the underlying assemblies are semantically identical. Hashing
 * the raw response text made the spec-parity check (ADR 0022) sensitive
 * to that incidental ordering, producing false "drift" failures in CI
 * that couldn't be reproduced locally. Array order is preserved —
 * arrays (enum values, parameter lists, required-field lists) carry
 * real meaning and reordering them would hide genuine contract changes.
 */
export function canonicalize(value) {
  if (Array.isArray(value)) {
    return value.map(canonicalize);
  }
  if (value !== null && typeof value === 'object') {
    const sorted = {};
    for (const key of Object.keys(value).sort()) {
      sorted[key] = canonicalize(value[key]);
    }
    return sorted;
  }
  return value;
}

export function canonicalJsonHash(createHash, specText) {
  const canonical = JSON.stringify(canonicalize(JSON.parse(specText)));
  return createHash('sha256').update(canonical).digest('hex');
}
