using System.Text.Json.Serialization;

namespace Norse.Infrastructure.Web.Server.Json;

/// <summary>
/// Composition-root wiring for Futhark's JSON leg (spec §9.1): the <see cref="Norse.Primitives.Result{T}"/> funnel
/// converter family, the §7 lexical pinning for plain DateTime/DateTimeOffset/TimeOnly/TimeSpan
/// scalars, and the <see cref="JsonUnmappedMemberHandling.Disallow"/> strictness ratchet that keeps
/// JSON's unknown-member posture aligned with XML's (spec §8.1) — "strictness parity across text
/// channels is ratcheted up, not down."
/// </summary>
public static class MvcBuilderExtensions
{
	extension(IMvcBuilder builder)
	{
		/// <summary>Registers Futhark's JSON converters and strictness settings on the MVC JSON options.</summary>
		public IMvcBuilder AddNorseJson()
		{
			builder.AddJsonOptions(options =>
			{
				options.JsonSerializerOptions.Converters.Add(new ResultJsonConverterFactory());
				options.JsonSerializerOptions.Converters.Add(new DateTimeLexicalJsonConverter());
				options.JsonSerializerOptions.Converters.Add(new DateTimeOffsetLexicalJsonConverter());
				options.JsonSerializerOptions.Converters.Add(new TimeOnlyLexicalJsonConverter());
				options.JsonSerializerOptions.Converters.Add(new TimeSpanLexicalJsonConverter());
				options.JsonSerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
			});
			return builder;
		}
	}
}
