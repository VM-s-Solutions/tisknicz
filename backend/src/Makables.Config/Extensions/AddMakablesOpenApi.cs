using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http;
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
///
/// <para>
/// <b>Multipart request bodies.</b> <see cref="Microsoft.AspNetCore.OpenApi"/>
/// emits <see cref="IFormFile"/> parameters by inlining the whole
/// <c>IFormFile</c> interface (<c>contentDisposition</c>, <c>headers</c>,
/// <c>length</c>, <c>name</c>, …) into the request body schema, which
/// makes NSwag's Fetch template emit a synthetic <c>Body</c> class. Even
/// in the bare-parameter case (today's <c>UploadImage</c>) the file
/// property is emitted as <c>{ "type": "string" }</c> with no
/// <c>format: "binary"</c> marker and no <c>required</c> entry — so the
/// generated client types the parameter as <c>FileParameter | undefined</c>
/// instead of the contract's required binary upload. T-0049b Copilot
/// review M3 acknowledged the gap; T-0049c (this transformer) closes it.
/// </para>
///
/// <para>
/// The operation transformer below detects request bodies with content
/// type <c>multipart/form-data</c>, finds every property whose source
/// CLR type is <see cref="IFormFile"/> (via
/// <see cref="Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription.ParameterDescriptions"/>),
/// rewrites those property schemas to the canonical OpenAPI 3.0
/// multipart-file shape <c>{ "type": "string", "format": "binary" }</c>,
/// and adds them to the schema's <c>required</c> collection. Non-file
/// properties (e.g. a future <c>Caption: string</c> alongside
/// <c>File: IFormFile</c> on a wrapper DTO — T-0064) are left untouched.
/// <c>application/json</c> bodies are skipped — only the multipart
/// branch is rewritten. Applied globally so all four hosts inherit the
/// fix via their existing <c>AddMakablesOpenApi("v1")</c> call.
/// </para>
///
/// <para>
/// Operation-level vs schema-level: an operation transformer is the
/// right granularity for "every action that takes <see cref="IFormFile"/>"
/// because the multipart media-type marker lives on
/// <see cref="OpenApiRequestBody"/>, not on the schema itself, and the
/// CLR parameter types are available on
/// <see cref="Microsoft.AspNetCore.Mvc.ApiExplorer.ApiDescription"/> —
/// not on <c>JsonTypeInfo</c> (the schema transformer's context). A
/// schema transformer would need to string-match property names, which
/// is fragile.
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

            options.AddOperationTransformer((operation, context, _) =>
            {
                // Only multipart bodies are rewritten. JSON/text bodies pass
                // through unchanged — declaring this guard first means the
                // transformer is a no-op for the overwhelming majority of
                // actions (every [FromBody] mutation across all four hosts).
                if (operation.RequestBody?.Content is null ||
                    !operation.RequestBody.Content.TryGetValue("multipart/form-data", out var mediaType) ||
                    mediaType?.Schema is not OpenApiSchema multipartSchema)
                {
                    return Task.CompletedTask;
                }

                // Collect every IFormFile parameter on this action by CLR
                // type, not by name. Surviving DTO renames and parameter
                // renames is the whole point of going through the
                // ApiDescription rather than string-matching the schema's
                // property names.
                var fileParameterNames = new List<string>();
                foreach (var parameter in context.Description.ParameterDescriptions)
                {
                    if (parameter.Type is not null && typeof(IFormFile).IsAssignableFrom(parameter.Type))
                    {
                        fileParameterNames.Add(parameter.Name);
                    }
                }

                if (fileParameterNames.Count == 0)
                {
                    return Task.CompletedTask;
                }

                // Rewrite-then-merge, not rewrite-from-scratch. The
                // emitter's output is the base; we only modify the file
                // property's schema (type + format) and ensure it's in
                // required. Non-file properties on a wrapper DTO (T-0064's
                // future File + Caption shape) stay typed as the emitter
                // chose. Same code path handles the bare-IFormFile case
                // (one property) and the wrapper-DTO case (many).
                multipartSchema.Type = JsonSchemaType.Object;
                multipartSchema.Properties ??= new Dictionary<string, IOpenApiSchema>(StringComparer.Ordinal);
                multipartSchema.Required ??= new HashSet<string>(StringComparer.Ordinal);

                foreach (var name in fileParameterNames)
                {
                    // If the emitter already created a property entry (the
                    // common case — it sees the IFormFile parameter and
                    // emits a property), replace its schema in place; the
                    // canonical OpenAPI 3.0 shape is `string` + binary.
                    // If for some reason no entry exists, insert one.
                    multipartSchema.Properties[name] = new OpenApiSchema
                    {
                        Type = JsonSchemaType.String,
                        Format = "binary",
                    };
                    multipartSchema.Required.Add(name);
                }

                // The request body itself is required when at least one
                // file is required — every multipart action with an
                // IFormFile parameter expects an actual upload.
                if (operation.RequestBody is OpenApiRequestBody mutableBody)
                {
                    mutableBody.Required = true;
                }

                return Task.CompletedTask;
            });
        });
        return services;
    }
}
