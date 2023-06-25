namespace IronMonkey.Api.Entities.Records;

public class RecordDetail {
    public int LeadId { get; set; }

    public IEnumerable<Field> Fields{ get; set; } = new List<Field>();
}