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
    public DbSet<ChatSession> ChatSessions => Set<ChatSession>();
    public DbSet<ChatMessage> ChatMessages => Set<ChatMessage>();
    public DbSet<AiLog> AiLogs => Set<AiLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<SupportTicket>().Property(ticket => ticket.Channel).HasDefaultValue("web");
        modelBuilder.Entity<ChatSession>().HasIndex(session => session.PublicId).IsUnique();
        modelBuilder.Entity<ChatSession>().HasIndex(session => session.TicketId);
        modelBuilder.Entity<ChatMessage>()
            .HasOne(message => message.Session)
            .WithMany(session => session.Messages)
            .HasForeignKey(message => message.SessionId)
            .OnDelete(DeleteBehavior.Cascade);
        modelBuilder.Entity<ChatMessage>().HasIndex(message => new { message.SessionId, message.Id });
        modelBuilder.Entity<AiLog>().HasIndex(log => log.SessionId);
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

        modelBuilder.Entity<SupportTicket>()
            .HasOne(ticket => ticket.Assignee)
            .WithMany()
            .HasForeignKey(ticket => ticket.AssigneeId)
            .OnDelete(DeleteBehavior.SetNull);
        modelBuilder.Entity<SupportTicket>().HasIndex(ticket => ticket.Status);

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
