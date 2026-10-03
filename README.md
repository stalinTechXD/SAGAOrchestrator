# Saga Pattern Demo — Orchestration vs. Choreography

This solution contains **two runnable ASP.NET Core (.NET 10) apps** that implement the **same** distributed
order workflow using the **Saga pattern**, so you can compare the two coordination styles. Both use
**SQL Server (EF Core)** for persistence.

Business transaction (all steps succeed, or earlier ones are compensated in reverse):

**Reserve inventory → Charge payment → Create shipment**

| Project | Style | Who decides the next step? | Messaging |
|---------|-------|----------------------------|-----------|
| `SAGAPATTERN.csproj` (root) | **Orchestration** | A central `OrderSagaOrchestrator` | Service Bus **queues** (command/reply) |
| [`SagaChoreography/`](SagaChoreography/README.md) | **Choreography** | Nobody — services react to events | Service Bus **topic** (pub/sub) |

> This README documents the **orchestration** app. See
> [`SagaChoreography/README.md`](SagaChoreography/README.md) for the choreography app.

---

# Order Saga Demo (Orchestration Pattern)

An ASP.NET Core (.NET 10) app demonstrating the **Saga pattern** for a distributed order workflow, with
**SQL Server** persistence (EF Core) and **Azure Service Bus** queues for messaging.

## Architecture

```
POST /orders
	 |
	 v
 Orchestrator --ReserveInventory--> [inventory-commands] --> Inventory participant
	 ^                                                           |
	 |<------------------------ [saga-replies] <-----------------+
	 |--ChargePayment----------> [payment-commands]   --> Payment participant   --> replies
	 |--CreateShipment---------> [shipping-commands]  --> Shipping participant   --> replies
```

| Step | Action                | Compensation       |
|------|-----------------------|--------------------|
| 1    | Reserve inventory     | Release inventory  |
| 2    | Charge payment        | Refund payment     |
| 3    | Create shipment       | (last step)        |

On failure the orchestrator runs compensations in reverse order. Saga state is stored on the `Orders` table
and every transition is appended to `SagaLog`. Participant handlers are idempotent so Service Bus
at-least-once delivery is safe.

Code layout:
- `Saga/OrderSagaOrchestrator.cs` - state machine and compensation logic
- `Saga/Participants.cs` - local transactions of Inventory, Payment, Shipping
- `Saga/SagaHost.cs` - wires queues to handlers
- `Messaging/` - `IMessageBus`, Service Bus and in-memory implementations
- `Data/`, `Domain/` - EF Core context and entities

> In this demo all participants live in one process for simplicity. Each could be split into its own
> service (own database) by running only its queue handler there.

## Run

1. SQL Server: default is LocalDB (`ConnectionStrings:SagaDb`). The schema and seed products are created on startup.
2. Messaging:
   - **No config**: an in-memory bus is used (works out of the box).
   - **Azure Service Bus**: create a namespace and four queues, then set the connection string:
	 ```
	 az servicebus namespace create -g <rg> -n <ns> --sku Basic
	 az servicebus queue create -g <rg> --namespace-name <ns> -n inventory-commands
	 az servicebus queue create -g <rg> --namespace-name <ns> -n payment-commands
	 az servicebus queue create -g <rg> --namespace-name <ns> -n shipping-commands
	 az servicebus queue create -g <rg> --namespace-name <ns> -n saga-replies
	 dotnet user-secrets set "ServiceBus:ConnectionString" "<connection string>"
	 ```
3. `dotnet run`

## Try the scenarios

Seed stock: LAPTOP=10, PHONE=5, MOUSE=100.

```
# Success
POST /orders {"sku":"MOUSE","quantity":2,"amount":40,"address":"1 Main St"}
# Payment fails (amount > 1000) -> inventory released
POST /orders {"sku":"LAPTOP","quantity":1,"amount":1500,"address":"1 Main St"}
# Shipping fails (address contains FAIL) -> payment refunded, inventory released
POST /orders {"sku":"PHONE","quantity":1,"amount":500,"address":"FAIL street"}
# Inventory fails -> cancelled immediately
POST /orders {"sku":"PHONE","quantity":99,"amount":10,"address":"1 Main St"}
```

Then `GET /orders/{id}` to see the final status and the full saga log, and `GET /products` to confirm stock was restored.
