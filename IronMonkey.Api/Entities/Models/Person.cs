namespace IronMonkey.Api.Entities.Models
{
    public class Person
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public DateTime DateOfBirth { get; set; }
        public string PlaceOfBirth { get; set; }
        public string Sex { get; set; }
        public int? FatherId { get; set; }
        public int? MotherId { get; set; }
        public virtual Person Father { get; set; }
        public virtual Person Mother { get; set; }
        public virtual ICollection<Person> Children { get; set; }
    }
}