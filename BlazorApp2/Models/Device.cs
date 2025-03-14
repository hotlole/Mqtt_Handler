namespace BlazorApp2.Models
{
    public class Device
    {
        public string Name { get; set; } = "";
        public string Status { get; set; } = "offline";
        public bool IsSelected { get; set; }
        public string Id { get; } = Guid.NewGuid().ToString();
    }
}
