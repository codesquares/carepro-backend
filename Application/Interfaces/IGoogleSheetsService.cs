namespace Application.Interfaces;

public interface IGoogleSheetsService
{
    Task AppendSignupDataAsync(string firstName, string lastName, string phoneNumber, string email, string userType);
}
