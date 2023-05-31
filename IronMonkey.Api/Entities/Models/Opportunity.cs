namespace IronMonkey.Api.Entities.Models;

public class Opportunity
{
    public int Id {get; set;}
    public DateTime CreatedAt {get; set;}
    public DateTime UpdatedAt {get; set;}
    public int CreatedBy {get; set;}
    public int UpdatedBy {get; set;}
}