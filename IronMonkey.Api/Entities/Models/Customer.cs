namespace IronMonkey.Api.Entities.Models;

 public class Customer
{
    public int Id {get; set;}
    public string? FirstName { get; set; }
    public string? LastName { get; set; }
    public string Mobile {get; set;}
    public DateTime CreatedAt {get; set;}
    public DateTime UpdatedAt {get; set;}
}