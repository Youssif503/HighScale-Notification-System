using Microsoft.EntityFrameworkCore;
using NotificationSystem.Models;
namespace NotificationSystem.Data
{
    public class ApplicationDbContext:DbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> op):base(op)
        {
            
        }
        public DbSet<User> Users { get; set; }
        public DbSet<Notification> Notifications { get; set; }
    }
}