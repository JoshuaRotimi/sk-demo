from semantic_kernel.plugins.core.kernel_plugin import KernelPlugin
from semantic_kernel.functions.kernel_function import kernel_function

class KYCRiskPlugin(KernelPlugin):

    @kernel_function(name="validate_id_document")
    def validate_id_document(self, document_text: str) -> str:
        if "forged" in document_text.lower():
            return "❌ Document invalid: suspected forgery."
        return "✅ Document valid."

    @kernel_function(name="screen_against_sanctions")
    def screen_against_sanctions(self, full_name: str) -> str:
        watchlist = ["John Doe", "Ali Khan"]
        return f"❌ {full_name} flagged on sanctions list." if full_name in watchlist else f"✅ {full_name} is clear."

    @kernel_function(name="assess_behavioral_risk")
    def assess_behavioral_risk(self, behavior_data: str) -> str:
        if "bot-like" in behavior_data.lower() or "unusual" in behavior_data.lower():
            return "⚠️ Behavioral risk detected."
        return "✅ Behavior appears normal."

    @kernel_function(name="combine_risk_signals")
    def combine_risk_signals(self, id_status: str, sanctions_status: str, behavior_status: str) -> str:
        red_flags = [id_status, sanctions_status, behavior_status]
        if any("❌" in flag or "⚠️" in flag for flag in red_flags):
            return "🚨 High Risk: Escalate to manual review."
        return "✅ Low Risk: Auto-approve onboarding."
