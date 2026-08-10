namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
///     A single read-path failure, located by its §11.2 path grammar (e.g. <c>Policy/Coverage[2]/@limit</c>)
///     and rendered through <see cref="FailureDetail.Render" />. Deliberately <see langword="public" />, not
///     <see langword="internal sealed" />: generated code in a host compilation (a different repo, later task)
///     constructs and reads this type, so it must be visible outside this assembly.
/// </summary>
/// <param name="Path">The §11.2 path grammar location of the failure.</param>
/// <param name="Detail">The rendered, human-readable failure detail.</param>
public readonly record struct XmlReadFailure(string Path, string Detail);
