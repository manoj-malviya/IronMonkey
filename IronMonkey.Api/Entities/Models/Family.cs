namespace IronMonkey.Api.Entities.Models
{
    public class IronMonkey
    {
        public int Id { get; set; }
        public Person Husband { get; set; }
        public Person Wife { get; set; }
        public List<Person> Children { get; set; }
    }
}