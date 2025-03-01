namespace Idealista.Models
{
    public class Models
    {
        public class Property
        {
            public bool IsProfessional { get; set; } = true;
            public string Id { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
            public string Title { get; set; } = string.Empty;
            public string Location { get; set; } = string.Empty;
            public int Price { get; set; }
            public string Currency { get; set; } = string.Empty;
            public string Description { get; set; } = string.Empty;
        }

        public class Client
        {
            public required string Id { get; set; }
            public required string Name { get; set; }
            public required string Email { get; set; }
        }

        public class Subscription
        {
            public required string Id { get; set; }
            public required string ClientId { get; set; }
            public required string City { get; set; }
        }
        public class City
        {
            public string Name { get; set; } = string.Empty;
            public string Url { get; set; } = string.Empty;
        }

        public class CitiesConfig
        {
            public List<City> Cities { get; set; } = new();
        }
    }
}
