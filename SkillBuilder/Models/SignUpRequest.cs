using System.ComponentModel.DataAnnotations;

namespace SkillBuilder.Models
{
    public class SignupRequest
    {
        public string FirstName { get; set; }

        public string LastName { get; set; }

        public string Email { get; set; }

        public string Password { get; set; }

        public DateOnly BirthDate { get; set; }
    }
}