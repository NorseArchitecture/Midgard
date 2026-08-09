using System.Diagnostics;
using System.Globalization;
using FluentValidation;
using Norse.Infrastructure.Web.Server.Xml;
using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Validation;

/// <summary>
///     FluentValidation rules for <see cref="Result{T}" /> members — the absent-member semantics
///     <c>Infrastructure.Web.Grpc</c>'s <c>ResultSerializer{T}</c> deliberately leaves to this layer
///     (protobuf-net hands back <c>default(Result{T})</c> for a field whose tag never appeared on the
///     wire; it does not — and, being a pure wire mechanism, cannot — know whether that absence is a
///     validation failure). <b>One-message-source condition:</b> the required rule's default-state
///     message is obtained by literally calling <see cref="Parser.ParseRequired{T}" /> with empty input
///     and rendering the resulting <see cref="Failure" /> through <see cref="FailureDetail.Render" /> —
///     the exact function <c>XmlReadContext.AddScalarFailure</c> (Task 1) calls for the same wording on
///     the XML/JSON channels — so "required value missing" is byte-identical across every channel by
///     construction, never a paraphrase.
/// </summary>
public static class ResultRules
{
	/// <summary>
	///     Requires the rule's <see cref="Result{T}" /> member to be Success-cased. Fails on default-state
	///     (never set — the wire's "absent") or Failure-state, in both cases rendering through
	///     <see cref="FailureDetail.Render" /> so the message text matches the other two channels exactly.
	/// </summary>
	public static IRuleBuilderOptions<T, Result<TValue>> ResultRequired<T, TValue>(
		this IRuleBuilder<T, Result<TValue>> ruleBuilder)
		where TValue : notnull, ISpanParsable<TValue> =>
		ruleBuilder.Must(static result => result.TryGetValue(out Success<TValue> _))
			.WithMessage(static (_, result) => Render(result));

	/// <summary>
	///     Allows the rule's <c>Result&lt;TValue&gt;?</c> member to be absent (<see langword="null" />),
	///     but fails when present and Failure-cased, rendering through <see cref="FailureDetail.Render" />
	///     exactly as <see cref="ResultRequired{T,TValue}" /> does for the required case.
	/// </summary>
	public static IRuleBuilderOptions<T, Result<TValue>?> ResultOptional<T, TValue>(
		this IRuleBuilder<T, Result<TValue>?> ruleBuilder)
		where TValue : notnull, ISpanParsable<TValue> =>
		ruleBuilder.Must(static result => result is null || result.Value.TryGetValue(out Success<TValue> _))
			.WithMessage(static (_, result) => Render(result!.Value));

	/// <summary>
	///     Requires the rule's <c>Result&lt;TEnum&gt;</c> member to be Success-cased — the enum twin of
	///     <see cref="ResultRequired{T,TValue}" />. Named distinctly (not an overload): a same-name
	///     <c>ResultRequired&lt;T, TEnum&gt;(IRuleBuilder&lt;T, Result&lt;TEnum&gt;&gt;)</c> and this
	///     project's <see cref="ResultRequired{T,TValue}" /> erase to the identical parameter shape
	///     <c>IRuleBuilder&lt;T, Result&lt;X&gt;&gt;</c> — declaring both is CS0111 (duplicate member),
	///     not merely an ambiguous call site; verified with a scratch compile before choosing this name.
	/// </summary>
	public static IRuleBuilderOptions<T, Result<TEnum>> ResultRequiredEnum<T, TEnum>(
		this IRuleBuilder<T, Result<TEnum>> ruleBuilder)
		where TEnum : unmanaged, Enum =>
		ruleBuilder.Must(static result => result.TryGetValue(out Success<TEnum> _))
			.WithMessage(static (_, result) => RenderEnum(result));

	/// <summary>
	///     Allows the rule's <c>Result&lt;TEnum&gt;?</c> member to be absent (<see langword="null" />), but
	///     fails when present and Failure-cased — the enum twin of <see cref="ResultOptional{T,TValue}" />,
	///     named distinctly for the same CS0111 reason documented on <see cref="ResultRequiredEnum{T,TEnum}" />.
	/// </summary>
	public static IRuleBuilderOptions<T, Result<TEnum>?> ResultOptionalEnum<T, TEnum>(
		this IRuleBuilder<T, Result<TEnum>?> ruleBuilder)
		where TEnum : unmanaged, Enum =>
		ruleBuilder.Must(static result => result is null || result.Value.TryGetValue(out Success<TEnum> _))
			.WithMessage(static (_, result) => RenderEnum(result!.Value));

	static string Render<TValue>(Result<TValue> result) where TValue : notnull, ISpanParsable<TValue> =>
		FailureDetail.Render(result.TryGetValue(out Failure failure) ?
			failure :
			RequiredMissing<TValue>());

	static Failure RequiredMissing<TValue>() where TValue : notnull, ISpanParsable<TValue> =>
		Parser.ParseRequired<TValue>(string.Empty, CultureInfo.InvariantCulture).TryGetValue(out Failure failure) ?
			failure :
			throw new UnreachableException("Parser.ParseRequired with empty input must always fail.");

	/// <summary>
	///     Renders an enum <see cref="Result{T}" />'s failure (or its default-state/absent stand-in) through
	///     <see cref="FailureDetail.Render" /> — the same one-message-source condition as <see cref="Render{TValue}" />,
	///     minus the <see cref="ISpanParsable{TValue}" /> route: no enum can call <see cref="Parser.ParseRequired{T}" />
	///     (enums are not <see cref="ISpanParsable{TValue}" />), so the "required value missing" failure is
	///     constructed directly with <see cref="ParseFailure.Empty" /> — byte-identical wording to the scalar
	///     path by construction, since <see cref="FailureDetail.Render" /> ignores everything but the reason.
	/// </summary>
	static string RenderEnum<TEnum>(Result<TEnum> result) where TEnum : unmanaged, Enum =>
		FailureDetail.Render(result.TryGetValue(out Failure failure) ?
			failure :
			RequiredMissingEnum<TEnum>());

	static Failure RequiredMissingEnum<TEnum>() where TEnum : unmanaged, Enum =>
		new(ParseFailure.Empty, string.Empty, typeof(TEnum).Name);
}
