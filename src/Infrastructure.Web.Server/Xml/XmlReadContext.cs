using Norse.Primitives;

namespace Norse.Infrastructure.Web.Server.Xml;

/// <summary>
///     Tracks the current read position through an XML document as a segment stack, rendering §11.2
///     path grammar (e.g. <c>Policy/Coverage[2]/@limit</c>) and accumulating <see cref="XmlReadFailure" />
///     entries as scalar conversions fail, rather than throwing on the first one. Deliberately
///     <see langword="public" />, not <see langword="internal sealed" />: generated code in a host
///     compilation (a different repo, later task) constructs and drives this type, so it must be visible
///     outside this assembly.
/// </summary>
public sealed class XmlReadContext
{
	readonly List<XmlReadFailure> _failures = [];
	readonly List<(string Name, int Index)> _segments = [];

	/// <summary>The current element/item path, e.g. <c>Policy/Coverage[2]</c>.</summary>
	public string CurrentPath =>
		string.Join('/', _segments.Select(static segment =>
			segment.Index > 0 ?
				$"{segment.Name}[{segment.Index}]" :
				segment.Name));

	/// <summary>Whether any failures have been accumulated.</summary>
	public bool HasFailures =>
		_failures.Count > 0;

	/// <summary>The accumulated failures, in the order they were recorded.</summary>
	public IReadOnlyList<XmlReadFailure> Failures =>
		_failures;

	/// <summary>Pushes a root or nested element onto the path. Root first; nested elements after.</summary>
	public void PushElement(string wireName) =>
		_segments.Add((wireName, 0));

	/// <summary>Pushes a collection item onto the path. <paramref name="index" /> is 1-based.</summary>
	public void PushItem(string wireName, int index) =>
		_segments.Add((wireName, index));

	/// <summary>Pops the innermost segment, whichever <c>Push*</c> call pushed it.</summary>
	public void Pop() =>
		_segments.RemoveAt(_segments.Count - 1);

	/// <summary>
	///     Renders the current path with <paramref name="attributeName" /> appended as an attribute, e.g.
	///     <c>Policy/Coverage[2]/@limit</c>.
	/// </summary>
	public string PathTo(string attributeName) =>
		$"{CurrentPath}/@{attributeName}";

	/// <summary>Records a failure at an explicit path.</summary>
	public void AddFailure(string path, string detail) =>
		_failures.Add(new XmlReadFailure(path, detail));

	/// <summary>
	///     Records a scalar conversion failure at <paramref name="attributeName" /> under the current path, rendered
	///     through <see cref="FailureDetail.Render" />.
	/// </summary>
	public void AddScalarFailure(string attributeName, in Failure failure) =>
		AddFailure(PathTo(attributeName), FailureDetail.Render(failure));
}
