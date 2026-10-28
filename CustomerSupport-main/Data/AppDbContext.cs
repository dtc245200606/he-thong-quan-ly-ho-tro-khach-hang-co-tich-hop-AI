using CustomerSupportAI.Models;
using Microsoft.EntityFrameworkCore;

namespace CustomerSupportAI.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<SupportUser> Users => Set<SupportUser>();
    public DbSet<SupportTicket> Tickets => Set<SupportTicket>();
    public DbSet<TicketMessage> TicketMessages => Set<TicketMessage>();
    public DbSet<KnowledgeArticle> KnowledgeArticles => Set<KnowledgeArticle>();
    public DbSet<AssistantTurn> AssistantTurns => Set<AssistantTurn>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AssistantTurn>().HasIndex(turn => new { turn.UserId, turn.Id });
        modelBuilder.Entity<AssistantTurn>().HasOne<SupportUser>().WithMany().HasForeignKey(turn => turn.UserId).OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<SupportUser>()
            .HasIndex(user => user.NormalizedEmail)
            .IsUnique();

        modelBuilder.Entity<SupportTicket>()
            .HasIndex(ticket => ticket.TicketNumber)
            .IsUnique();

        modelBuilder.Entity<SupportTicket>()
            .HasOne(ticket => ticket.Customer)
            .WithMany()
            .HasForeignKey(ticket => ticket.CustomerId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TicketMessage>()
            .HasOne(message => message.Author)
            .WithMany()
            .HasForeignKey(message => message.AuthorId)
            .OnDelete(DeleteBehavior.Restrict);

        modelBuilder.Entity<TicketMessage>()
            .HasOne(message => message.Ticket)
            .WithMany(ticket => ticket.Messages)
            .HasForeignKey(message => message.TicketId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}
