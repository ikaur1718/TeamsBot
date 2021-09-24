using Microsoft.Bot.Builder;
using Microsoft.Bot.Builder.Dialogs;
using Microsoft.Bot.Schema;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Microsoft.Recognizers.Text.DataTypes.TimexExpression;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using TeamsBot.Graph;
using TimexTypes = Microsoft.Recognizers.Text.DataTypes.TimexExpression.Constants.TimexTypes;

namespace TeamsBot.Dialogs
{
    public class LookupUserwithPhoneDialog : LogoutDialog
    {
        protected readonly ILogger _logger;
        private readonly IGraphClientService _graphClientService;

        public LookupUserwithPhoneDialog(
            IConfiguration configuration,
            IGraphClientService graphClientService)
            : base(nameof(LookupUserwithPhoneDialog), configuration["ConnectionName"])
        {
            _graphClientService = graphClientService;

            // OAuthPrompt dialog handles the token
            // acquisition
            AddDialog(new OAuthPrompt(
                nameof(OAuthPrompt),
                new OAuthPromptSettings
                {
                    ConnectionName = ConnectionName,
                    Text = "Please login",
                    Title = "Login",
                    Timeout = 300000, // User has 5 minutes to login
                }));

            AddDialog(new TextPrompt("phonenumber"));
            AddDialog(new WaterfallDialog(nameof(WaterfallDialog), new WaterfallStep[]
            {
                DisplayUserwithPhoneNumber,
                GetTokenAsync,
                LookupwithPhonenumber
            }));

            // The initial child Dialog to run.
            InitialDialogId = nameof(WaterfallDialog);
        }

        // Generate a DateTime from the list of
        // DateTimeResolutions provided by the DateTimePrompt
        private static DateTime GetDateTimeFromResolutions(IList<DateTimeResolution> resolutions)
        {
            var timex = new TimexProperty(resolutions[0].Timex);

            // Handle the "now" case
            if (timex.Now ?? false)
            {
                return DateTime.Now;
            }

            // Otherwise generate a DateTime
            return TimexHelpers.DateFromTimex(timex);
        }

        private async Task<DialogTurnResult> DisplayUserwithPhoneNumber(
        WaterfallStepContext stepContext,
        CancellationToken cancellationToken)
        {
            await stepContext.BeginDialogAsync(nameof(OAuthPrompt), null, cancellationToken);

            return await stepContext.PromptAsync("phonenumber",
                new PromptOptions
                {
                    Prompt = MessageFactory.Text("What's the phone number for look up?")
                },
                cancellationToken);
        }

        private async Task<DialogTurnResult> GetTokenAsync(
        WaterfallStepContext stepContext,
        CancellationToken cancellationToken)
        {
            if ((string)stepContext.Result != null)
            {
                stepContext.Values["phoneNumber"] = (string)stepContext.Result;
                return await stepContext.BeginDialogAsync(nameof(OAuthPrompt), null, cancellationToken);
            }
            else
            {
                await stepContext.Context.SendActivityAsync(
                    MessageFactory.Text("Please try again."));

                return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
            }
        }

        private async Task<DialogTurnResult> LookupwithPhonenumber(
        WaterfallStepContext stepContext,
        CancellationToken cancellationToken)
        {
            if (stepContext.Result != null)
            {
                var tokenResponse = stepContext.Result as TokenResponse;
                if (tokenResponse?.Token != null)
                {
                    var graphClient = _graphClientService.GetAuthenticatedGraphClient(tokenResponse?.Token);
                    var users = await graphClient.Users
                        .Request()
                        .GetAsync();

                    var phones = users.Select(u => new { u.BusinessPhones, u.DisplayName }).ToList();
                    var user = phones.Where(t => t.BusinessPhones.Equals(stepContext.Values["phoneNumber"])).Select(u => u.DisplayName);
                    return await stepContext.EndDialogAsync(user.ToString(), cancellationToken);
                }
            }
            await stepContext.Context.SendActivityAsync(
            MessageFactory.Text("We couldn't log you in. Please try again later."),
            cancellationToken);
            return await stepContext.EndDialogAsync(cancellationToken: cancellationToken);
        }

        private static bool TimexHasDateAndTime(TimexProperty timex)
        {
            return timex.Now ?? false ||
                (timex.Types.Contains(TimexTypes.DateTime) &&
                timex.Types.Contains(TimexTypes.Definite));
        }

        private static Task<bool> StartPromptValidatorAsync(
            PromptValidatorContext<IList<DateTimeResolution>> promptContext,
            CancellationToken cancellationToken)
        {
            if (promptContext.Recognized.Succeeded)
            {
                // Initialize a TimexProperty from the first
                // recognized value
                var timex = new TimexProperty(
                    promptContext.Recognized.Value[0].Timex);

                // If it has a definite date and time, it's valid
                return Task.FromResult(TimexHasDateAndTime(timex));
            }

            return Task.FromResult(false);
        }

        private static Task<bool> EndPromptValidatorAsync(
        PromptValidatorContext<IList<DateTimeResolution>> promptContext,
        CancellationToken cancellationToken)
        {
            if (promptContext.Recognized.Succeeded)
            {
                if (promptContext.Options.Validations is DateTime start)
                {
                    // Initialize a TimexProperty from the first
                    // recognized value
                    var timex = new TimexProperty(
                        promptContext.Recognized.Value[0].Timex);

                    // Get the DateTime from this value to compare with start
                    var end = GetDateTimeFromResolutions(promptContext.Recognized.Value);

                    // If it has a definite date and time, and
                    // the value is later than start, it's valid
                    return Task.FromResult(TimexHasDateAndTime(timex) &&
                        DateTime.Compare(start, end) < 0);
                }
            }

            return Task.FromResult(false);
        }
    }
}

