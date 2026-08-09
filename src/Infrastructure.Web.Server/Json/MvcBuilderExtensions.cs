using System.Diagnostics.CodeAnalysis;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Options;
using Norse.Infrastructure.Backend.Serialization;
using Norse.Infrastructure.Web.Server.Xml;

namespace Norse.Infrastructure.Web.Server.Json;

/// <summary>
///     Composition-root wiring for Futhark's JSON leg (spec §9.1): the <see cref="Norse.Primitives.Result{T}" /> funnel
///     converter family, the §7 lexical pinning for plain DateTime/DateTimeOffset/TimeOnly/TimeSpan
///     scalars, the plain and <see cref="Norse.Primitives.Result{T}" />-wrapped enum wire law over the
///     generated <see cref="EnumNameRegistry" /> (spec §7.4), the <see cref="OptInContractModifier" /> that
///     enforces the <c>[DataContract]</c> opt-in law (spec §4b) — three serializers, one membership
///     definition, STJ made to honor the WCF vocabulary it ignores natively — and the
///     <see cref="JsonUnmappedMemberHandling.Disallow" /> strictness ratchet that keeps JSON's
///     unknown-member posture aligned with XML's (spec §8.1) — "strictness parity across text channels is
///     ratcheted up, not down."
/// </summary>
public static class MvcBuilderExtensions
{
	extension(IMvcBuilder builder)
	{
		/// <summary>
		///     Registers Futhark's JSON converters and strictness settings on the MVC JSON options, styled by
		///     the same <see cref="NorseXmlOptions" /> the XML leg composes from — this seam resolves it from
		///     DI rather than taking its own copy, so the case style is stated once, by the host's call to
		///     the Xml namespace's own <c>AddNorseXml</c>. A host wiring JSON without ever calling
		///     <c>AddNorseXml</c> fails loud at startup resolving <see cref="IOptions{TOptions}" /> of
		///     <see cref="NorseXmlOptions" /> — no silent numeric-enum or unstyled fallback.
		/// </summary>
		/// <param name="registry">
		///     The generated per-enum name-table registry — the host passes
		///     <c>NorseEnumNameRegistration.Build()</c> (a later task's generator emission).
		/// </param>
		[UnconditionalSuppressMessage("Trimming", "IL2026",
			Justification =
				"Same finite, reflection-free posture as ResultJsonConverter<T>.WritePresent: DefaultJsonTypeInfoResolver here only feeds OptInContractModifier's attribute inspection, no unbounded reflection surface to trim.")]
		[UnconditionalSuppressMessage("AotAnalysis", "IL3050",
			Justification =
				"Same posture as ResultJsonConverterFactory.CreateConverter: composition-root wiring, not a generic-parameter-driven reflection surface; AOT source-generation for the resolver chain is a future increment.")]
		public IMvcBuilder AddNorseJson(EnumNameRegistry registry)
		{
			ArgumentNullException.ThrowIfNull(registry);

			builder.Services.AddSingleton(registry);
			builder.Services.AddOptions<JsonOptions>().Configure<NorseXmlOptions>((options, xmlOptions) =>
			{
				options.JsonSerializerOptions.TypeInfoResolver =
					(options.JsonSerializerOptions.TypeInfoResolver ?? new DefaultJsonTypeInfoResolver())
					.WithAddedModifier(OptInContractModifier.Apply);
				options.JsonSerializerOptions.Converters.Add(new ResultJsonConverterFactory());
				options.JsonSerializerOptions.Converters.Add(new MaskedValueJsonConverterFactory());
				options.JsonSerializerOptions.Converters.Add(new DateTimeLexicalJsonConverter());
				options.JsonSerializerOptions.Converters.Add(new DateTimeOffsetLexicalJsonConverter());
				options.JsonSerializerOptions.Converters.Add(new TimeOnlyLexicalJsonConverter());
				options.JsonSerializerOptions.Converters.Add(new TimeSpanLexicalJsonConverter());
				options.JsonSerializerOptions.Converters.Add(new EnumLexicalJsonConverterFactory(registry, xmlOptions));
				options.JsonSerializerOptions.Converters.Add(new ResultEnumJsonConverterFactory(registry, xmlOptions));
				options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
			});
			return builder;
		}
	}
}
