using System.Diagnostics.CodeAnalysis;

namespace Octans.Core.Models.Tagging;

[SuppressMessage("Naming", "CA1716:Identifiers should not match keywords", Justification = "Required by domain")]
public class Namespace
{
    public int Id { get; set; }
    public required string Value { get; set; }
}