using Microsoft.SemanticKernel;
using System.ComponentModel;

namespace ChatApp.Server.Plugins;

public class ValidatePersonPlugin()
{
    [KernelFunction("ValidateMember")]
    [Description("Find and validate a person in SystemOne or SystemTwo by card number.")]
    [return: Description("string result of validation attempt")]
    public async Task<string> ValidateMember(string cardNumber)
    {
        await Task.Delay(0);
        switch (cardNumber.ToLower())
        {
            case "abcd1234":
                return "Person validated in SystemOne";
            case "xyz8675309":
                return "Person validated in SystemTwo";
            default:
                return "Person not found in any system";
        }
    }
}
