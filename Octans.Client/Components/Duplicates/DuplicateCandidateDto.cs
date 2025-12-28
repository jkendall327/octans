using Octans.Core.Models;
using Octans.Core.Models.Duplicates;

namespace Octans.Client.Components.Duplicates;

public class DuplicateCandidateDto
{
    public int Id { get; set; }
    public int HashId1 { get; set; }
    public string Url1 { get; set; } = string.Empty;
    public int HashId2 { get; set; }
    public string Url2 { get; set; } = string.Empty;
    public double Distance { get; set; }
}
