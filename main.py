import json
import asyncio

from semantic_kernel import Kernel
from semantic_kernel.connectors.ai.open_ai import AzureChatCompletion
from semantic_kernel.connectors.ai.chat_completion_client_base import ChatHistory

from plugins.kyc_risk_plugin import KYCRiskPlugin

# Load configuration
with open("config.json", "r") as f:
    config = json.load(f)

model_id = config["modelId"]
endpoint = config["endpoint"]
api_key = config["apiKey"]

# Initialize kernel
kernel = Kernel()

chat_service = AzureChatCompletion(
    deployment_name=model_id,
    endpoint=endpoint,
    api_key=api_key
)
kernel.add_service(chat_service)

# Register plugin
kernel.add_plugin(KYCRiskPlugin(), plugin_name="kyc_risk")

# Chat loop
async def main():
    history = ChatHistory()

    print("KYC Assistant Ready. Type customer info for verification.\nPress Enter to exit.")

    while True:
        user_input = input("User: ").strip()
        if user_input == "":
            print("Session ended.")
            break

        # Simulate manual use of plugin functions
        plugin = kernel.plugins["kyc_risk"]

        doc_result = plugin["validate_id_document"].invoke("Forged national ID")
        sanction_result = plugin["screen_against_sanctions"].invoke("John Doe")
        behavior_result = plugin["assess_behavioral_risk"].invoke("Bot-like input pattern detected")

        combined = plugin["combine_risk_signals"].invoke(
            doc_result.return_value,
            sanction_result.return_value,
            behavior_result.return_value,
        )

        print("\nKYC Evaluation:")
        print(doc_result.return_value)
        print(sanction_result.return_value)
        print(behavior_result.return_value)
        print("➡", combined.return_value)
        print()

if __name__ == "__main__":
    asyncio.run(main())
