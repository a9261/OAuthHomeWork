using System;

namespace OAuthHomeWork.Models
{
    public class UserDataModel : IEquatable<UserDataModel>
    {
        public string UserId { get; set; }

        public string LoginToken { get; set; }
        public string NotifyToken { get; set; }

        public bool Equals(UserDataModel other)
        {
            return this.UserId == other.UserId;
        }
    }
}