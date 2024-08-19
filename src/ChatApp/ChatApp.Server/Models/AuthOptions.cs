namespace ChatApp.Server.Models;

public class AuthOptions  
{  
    public string url { get; set; } = string.Empty;
    public string clientId { get; set; } = string.Empty;
    public string clientSecret { get; set; } = string.Empty;
    public string scope { get; set; } = string.Empty;
    public string grantType { get; set; } = string.Empty;
}
