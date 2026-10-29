using SecureLab.Api.Application.Access;

namespace SecureLab.Api.Presentation.Endpoints;

// Одна форма відмов для всіх маршрутів інциденту. Політика: 403 для чужого запису,
// 404 лише для id, якого немає (обґрунтування в docs/access-matrix.md).
internal static class AccessProblems
{
    public static IResult IncidentMissing(Guid id) => Results.Problem(
        title: "Інцидент не знайдено",
        detail: $"Інцидент '{id}' не існує.",
        statusCode: StatusCodes.Status404NotFound);

    public static IResult CommentMissing(Guid id) => Results.Problem(
        title: "Коментар не знайдено",
        detail: $"Коментар '{id}' не існує.",
        statusCode: StatusCodes.Status404NotFound);

    public static IResult Forbidden() => Results.Problem(
        title: "Доступ до інциденту заборонено",
        detail: "Ваш обліковий запис не має права на цю дію з цим інцидентом.",
        statusCode: StatusCodes.Status403Forbidden);

    public static IResult ForIncident(AccessVerdict verdict, Guid id) =>
        verdict == AccessVerdict.Absent ? IncidentMissing(id) : Forbidden();

    // Сюди доходять лише автентифіковані запити. Без claim id це збій конфігурації, а не привід
    // шукати id у тілі чи заголовку.
    public static IResult NoVerifiedCaller() => Results.Problem(statusCode: StatusCodes.Status500InternalServerError);
}
