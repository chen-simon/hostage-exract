public interface IInteractable
{
    // Text shown in the prompt. usable=false shows the text greyed out and blocks interaction.
    string GetPrompt(out bool usable);
    float HoldTime { get; }
    void Interact();
}
