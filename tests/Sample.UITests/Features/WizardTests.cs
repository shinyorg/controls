namespace Sample.UITests.Features;

/// <summary>
/// Wizard on <c>wizard</c>: the email gate, Next/Back, the hidden Delivery step, finish and cancel.
/// Shell keeps the page (and its view-model) alive, so each test rewinds to the first step with an
/// empty email and delivery switched on.
/// </summary>
public class WizardTests : UITest
{
    const string Wizard = "DemoWizard";
    const string Status = "WizardStatusLabel";
    // the sample names its wizard DemoWizard, so its buttons are <id>_Next / _Back / _Cancel
    const string Next = "DemoWizard_Next";

    async Task OpenAtStart()
    {
        await Open("wizard", "WizardPage");
        await Driver.SetProperty("CurrentStep", "Account", automationId: Wizard);
        await Driver.WaitForProperty(Wizard, "CurrentStep", "Account");

        // through a non-empty value, so the status line is always rewritten (it only changes on a change)
        await Driver.SetProperty("Text", "reset", automationId: "WizardEmailEntry");
        await Driver.SetProperty("Text", "", automationId: "WizardEmailEntry");
        await Driver.WaitForProperty(Status, "Text", "Enter an email containing @.");

        // The delivery switch lives on the Fulfilment step, and only the current step's content is in
        // the tree - read the view-model through the page, and visit Fulfilment only to switch it back on.
        if (await Driver.GetProperty("BindingContext.WantsDelivery", automationId: "WizardPage") != "True")
        {
            await Driver.SetProperty("CurrentStep", "Fulfilment", automationId: Wizard);
            await Driver.SetProperty("IsToggled", "True", automationId: "WizardDeliverySwitch");
            await Driver.WaitForProperty("WizardPage", "BindingContext.WantsDelivery", "True");
            await Driver.SetProperty("CurrentStep", "Account", automationId: Wizard);
            await Driver.WaitForProperty(Wizard, "CurrentStep", "Account");
        }
    }

    async Task EnterEmail(string email)
    {
        await Driver.SetProperty("Text", email, automationId: "WizardEmailEntry");
        await Driver.WaitForProperty("WizardEmailEntry", "Text", email);
    }

    async Task GoNext(string expectedStep)
    {
        await Driver.Tap(automationId: Next);
        await Driver.WaitForProperty(Wizard, "CurrentStep", expectedStep);
    }

    [Test]
    public async Task Next_Stays_Disabled_Until_The_Email_Is_Plausible()
    {
        await OpenAtStart();
        await Driver.WaitForProperty(Next, "IsEnabled", "False");

        await EnterEmail("nope");
        await Driver.WaitForProperty(Status, "Text", "Enter an email containing @.");
        await Driver.WaitForProperty(Next, "IsEnabled", "False");

        await EnterEmail("ada@example.com");
        await Driver.WaitForProperty(Status, "Text", "Looks good — Next is enabled.");
        await Driver.WaitForProperty(Next, "IsEnabled", "True");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Next_And_Back_Walk_The_Steps()
    {
        await OpenAtStart();
        await EnterEmail("ada@example.com");

        await GoNext("Fulfilment");
        await Driver.WaitForProperty(Status, "Text", "On step: Fulfilment");
        await Driver.WaitForProperty(Wizard, "StepNumber", "2");

        await Driver.Tap(automationId: "DemoWizard_Back");
        await Driver.WaitForProperty(Wizard, "CurrentStep", "Account");
        await Driver.WaitForProperty(Wizard, "IsFirstStep", "True");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Hidden_Step_Leaves_The_Run_And_Is_Skipped()
    {
        await OpenAtStart();
        await Driver.WaitForProperty(Wizard, "StepCount", "4");
        await EnterEmail("ada@example.com");
        await GoNext("Fulfilment");

        await Driver.SetProperty("IsToggled", "False", automationId: "WizardDeliverySwitch");
        await Driver.WaitForProperty(Wizard, "StepCount", "3");

        await GoNext("Review");
        await Driver.WaitForProperty(Wizard, "IsLastStep", "True");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Optional_Delivery_Step_Can_Be_Passed_Empty()
    {
        await OpenAtStart();
        await EnterEmail("ada@example.com");
        await GoNext("Fulfilment");
        await GoNext("Delivery");

        // IsOptional: no address, and Next still moves on
        await GoNext("Review");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Finishing_Runs_FinishedCommand()
    {
        await OpenAtStart();
        await EnterEmail("grace@example.com");
        await GoNext("Fulfilment");
        await Driver.SetProperty("IsToggled", "False", automationId: "WizardDeliverySwitch");
        await GoNext("Review");

        await Driver.Tap(automationId: Next); // "Place order"
        await Driver.WaitForProperty(Status, "Text", "Submitted grace@example.com for collection");
        await AssertNoAppErrors();
    }

    [Test]
    public async Task Cancel_Runs_CancelledCommand()
    {
        await OpenAtStart();
        await Driver.Tap(automationId: "DemoWizard_Cancel");
        await Driver.WaitForProperty(Status, "Text", "Cancelled — nothing was submitted.");
        await AssertNoAppErrors();
    }
}
