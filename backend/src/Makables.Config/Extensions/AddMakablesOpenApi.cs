using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.OpenApi;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.OpenApi;

namespace Makables.Config.Extensions;

/// <summary>
/// Per-host OpenAPI registration with the Makables-wide schema fixups
/// every backend host needs to emit a spec that matches the runtime
/// JSON contract.
///
/// <para>
/// <b>String enums on the wire.</b> The MVC pipeline registers
/// <see cref="JsonStringEnumConverter"/> in
/// <see cref="MakablesControllersExtensions.AddMakablesControllers"/>,
/// so every enum reaches the wire as its name (<c>"Fixed"</c>, not
/// <c>0</c>). <see cref="Microsoft.AspNetCore.OpenApi"/>'s schema
/// generator builds from the type model only — it doesn't see the
/// converter — and emits enum schemas as <c>{ "type": "integer" }</c>,
/// which makes the generated TS clients type request-body enums as
/// <c>number</c> while the runtime accepts strings. The mismatch is
/// silent because <see cref="JsonStringEnumConverter"/> also accepts
/// integers, so a wrong-typed client still works; only the contract
/// lies. T-0049b Copilot review (priceType emitted as <c>number</c>
/// in <c>CreateProductRequest</c>/<c>UpdateProductRequest</c> while
/// read DTOs emit it as <c>string</c>).
/// </para>
///
/// <para>
/// The schema transformer below rewrites every C# enum schema to
/// <c>{ "type": "string", "enum": [&lt;names&gt;] }</c> so the spec
/// matches what the wire actually accepts and the NSwag-generated TS
/// client types the field as a string union. Applied globally because
/// the rule is platform-wide; every host imports this extension via
/// their <c>Program.cs</c>.
/// </para>
/// </summary>
public static class MakablesOpenApiExtensions
{
    public static IServiceCollection AddMakablesOpenApi(this IServiceCollection services, string documentName = "v1")
    {
        services.AddOpenApi(documentName, options =>
        {
            options.AddSchemaTransformer((schema, context, _) =>
            {
                var type = context.JsonTypeInfo.Type;
                if (type.IsEnum && schema is OpenApiSchema mutable)
                {
                    mutable.Type = JsonSchemaType.String;
                    mutable.Format = null;
                    var names = Enum.GetNames(type);
                    var nodes = new List<JsonNode>(names.Length);
                    foreach (var name in names)
                    {
                        nodes.Add(JsonValue.Create(name)!);
                    }
                    mutable.Enum = nodes;
                }
                return Task.CompletedTask;
            });
        });
        return services;
    }
}
