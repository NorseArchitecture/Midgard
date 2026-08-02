using System.Diagnostics;
using System.Globalization;
using FluentValidation;
using Norse.Infrastructure.Web.Server.Xml;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Validation;

/// <summary>
/// FluentValidation rules for <see cref="Result{T}"/> members — the absent-member semantics
/// <c>Infrastructure.Web.Grpc</c>'s <c>ResultSerializer{T}</c> deliberately leaves to this layer
/// (protobuf-net hands back <c>default(Result{T})</c> for a field whose tag never appeared on the
/// wire; it does not — and, being a pure wire mechanism, cannot — know whether that absence is a
/// validation failure). <b>One-message-source condition:</b> the required rule's default-state
/// message is obtained by literally calling <see cref="Parser.ParseRequired{T}"/> with empty input
/// and rendering the resulting <see cref="Failure"/> through <see cref="FailureDetail.Render"/> —
/// the exact function <c>XmlReadContext.AddScalarFailure</c> (Task 1) calls for the same wording on
/// the XML/JSON channels — so "required value missing" is byte-identical across every channel by
/// construction, never a paraphrase.
/// </summary>
public static class ResultRules
{
	/// <summary>
	/// Requires the rule's <see cref="Result{T}"/> member to be Success-cased. Fails on default-state
	/// (never set — the wire's "absent") or Failure-state, in both cases rendering through
	/// <see cref="FailureDetail.Render"/> so the message text matches the other two channels exactly.
	/// </summary>
	public static IRuleBuilderOptions<T, Result<TValue>> ResultRequired<T, TValue>(this IRuleBuilder<T, Result<TValue>> ruleBuilder)
		where TValue : notnull, ISpanParsable<TValue> =>
		ruleBuilder.Must(static result => result.TryGetValue(out Success<TValue> _)).WithMessage(static (_, result) => Render(result));

	/// <summary>
	/// Allows the rule's <c>Result&lt;TValue&gt;?</c> member to be absent (<see langword="null"/>),
	/// but fails when present and Failure-cased, rendering through <see cref="FailureDetail.Render"/>
	/// exactly as <see cref="ResultRequired{T,TValue}"/> does for the required case.
	/// </summary>
	public static IRuleBuilderOptions<T, Result<TValue>?> ResultOptional<T, TValue>(this IRuleBuilder<T, Result<TValue>?> ruleBuilder)
		where TValue : notnull, ISpanParsable<TValue> =>
		ruleBuilder.Must(static result => result is null || result.Value.TryGetValue(out Success<TValue> _))
			.WithMessage(static (_, result) => Render(result!.Value));

	static string Render<TValue>(Result<TValue> result) where TValue : notnull, ISpanParsable<TValue> =>
		FailureDetail.Render(result.TryGetValue(out Failure failure) ? failure : RequiredMissing<TValue>());

	static Failure RequiredMissing<TValue>() where TValue : notnull, ISpanParsable<TValue> =>
		Parser.ParseRequired<TValue>(string.Empty, CultureInfo.InvariantCulture).TryGetValue(out Failure failure) ?
			failure :
			throw new UnreachableException("Parser.ParseRequired with empty input must always fail.");
}
