using Microsoft.AspNetCore.OpenApi;
using Microsoft.OpenApi;

namespace Norse.Infrastructure.Web.Server.OpenApi;

/// <summary>
///     Registers the reusable bearer <c>securitySchemes</c> component once (Himinbjorg#49) — the scheme
///     declaration itself; <see cref="MachineAuthOperationTransformer" /> is what actually attaches the
///     requirement to individual operations, since a document transformer alone cannot tell a facade
///     operation from any other.
/// </summary>
public sealed class BearerSecuritySchemeTransformer : IOpenApiDocumentTransformer
{
	internal const string SchemeId = "Bearer";

	/// <inheritdoc />
	public Task TransformAsync(OpenApiDocument document, OpenApiDocumentTransformerContext context,
		CancellationToken cancellationToken)
	{
		ArgumentNullException.ThrowIfNull(document);

		document.Components ??= new OpenApiComponents();
		document.Components.SecuritySchemes ??= new Dictionary<string, IOpenApiSecurityScheme>();
		document.Components.SecuritySchemes[SchemeId] = new OpenApiSecurityScheme
		{
			Type = SecuritySchemeType.Http,
			Scheme = "bearer",
			BearerFormat = "JWT"
		};

		return Task.CompletedTask;
	}
}
