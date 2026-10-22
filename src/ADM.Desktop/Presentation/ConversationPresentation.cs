using ADM.Domain;

namespace ADM.Desktop.Presentation;

public static class ConversationPresentation
{
    public static bool ShouldNotify(ConversationMessage message) => !message.IsSystem &&
        (message.SemanticKey.StartsWith("services.start.", StringComparison.Ordinal) || message.SemanticKey == "services.starting" ||
         message.SemanticKey.StartsWith("disruption.", StringComparison.Ordinal) ||
         message.SemanticKey.StartsWith("boarding.approval", StringComparison.Ordinal) || message.SemanticKey.StartsWith("closure.approval", StringComparison.Ordinal) ||
         message.SemanticKey.StartsWith("handover.", StringComparison.Ordinal) || message.SemanticKey.StartsWith("service.completion.check", StringComparison.Ordinal));
}
