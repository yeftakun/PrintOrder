namespace PrintOrder
{
    internal sealed class AuthState
    {
        public string RefreshToken { get; set; } = string.Empty;
        public string? UserId { get; set; }
        public string? Username { get; set; }
    }
}
