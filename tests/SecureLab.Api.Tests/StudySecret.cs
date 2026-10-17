namespace SecureLab.Api.Tests;

// Навчальний пароль seed-акаунтів приходить лише зі змінної середовища поза Git.
// У повідомлення про помилку значення не потрапляє.
internal static class StudySecret
{
    public static string Password =>
        Environment.GetEnvironmentVariable("SeedUsers__Password")
        ?? throw new InvalidOperationException("Set local SeedUsers__Password outside Git.");
}
