namespace Paperless.REST.Models
{
    public class AccessStatistics
    {
        public int id { get; set; }
        public int documentId { get; set; }
        public DateTime accessDate { get; set; }
        public int accessCount { get; set; }
    }
}
