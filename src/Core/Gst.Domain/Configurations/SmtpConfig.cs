namespace Gst.Domain.Configurations
{
    // Host khali ho to email nahi jaata (dev me OTP Debug log me dikhta hai)
    public class SmtpConfig
    {
        public string Host { get; set; } = string.Empty;
        public int Port { get; set; } = 587;
        public bool EnableSsl { get; set; } = true;
        public string UserName { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FromEmail { get; set; } = string.Empty;
        public string FromName { get; set; } = string.Empty;

        // Set ho to mail bheji nahi jaati, .eml file is folder me likhi jaati hai (dev).
        // Relative path ContentRoot se resolve hota hai. Host se pehle check hota hai.
        public string PickupDirectory { get; set; } = string.Empty;
    }
}
