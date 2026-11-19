# AAX Warehouse Tools — Blind PO Receiving Setup Guide

This guide is for the implementer setting up **blind PO receiving**: receiving freight that arrives with no purchase order or advance notice. The lead creates an empty purchase order for each arrival. The warehouse worker builds its lines on the mobile device from what is actually delivered. A nightly batch posts the product receipt. For day-to-day use, see the [user guide](BlindReceivingUserGuide.md).

Blind PO receiving is switched on **per warehouse**. A warehouse without the setting receives exactly as it does today, and standard receiving menu items are untouched.

## 1. Prerequisites

- The `AAXWarehouseTools` package is deployed, including its `AAXBlindReceiving` model, and the database has been synchronized. Sync adds two fields to warehouses and two to purchase lines.
- After deployment, restart the AOS once so the new label texts load.
- Your user can maintain purchase pools, warehouses and mobile device menu items. To schedule the posting batch, your role needs the **Post blind receipts** duty (see section 10, Security).

## 2. Setup at a glance

1. Create a purchase pool for blind receipt orders — **Purchase pools**.
2. Turn blind PO receiving on for each receiving warehouse and name its pool — **Warehouses**.
3. Make sure change management can be off for blind receipt orders — **Procurement and sourcing parameters**.
4. Create the mobile device menu item and add it to the workers' menu — **Mobile device menu items** and **Mobile device menu**.
5. Check the standard setup the flow reuses — put-away, receipt location and document routing.
6. Schedule the nightly posting batch — **Post blind receipts**.
7. Decide how cancelled work is corrected — **Warehouse management parameters**.

## 3. Purchase pool

Blind receipt orders are told apart from every other purchase order by their purchase pool.

1. Go to **Procurement and sourcing > Setup > Purchase orders > Purchase pools**.
2. Create a pool, for example `BLIND` with the name *Blind receipt orders*.

Use the pool for blind receipt orders only. Every order in it, at a warehouse with the setting turned on, is treated as a blind receipt order. Warehouses can share one pool, or each can have its own.

## 4. Warehouse settings

1. Go to **Warehouse management > Setup > Warehouse > Warehouses** and select the warehouse.
2. On the **Warehouse** FastTab, find the **Blind PO receiving** group.
3. Set the fields below and save.

| Field | Meaning |
|---|---|
| **Enabled** | Turns blind PO receiving on for this warehouse. Off = the warehouse receives as standard, and its workers can't use the flow. |
| **Blind receipt pool** | The purchase pool that marks an order for this warehouse as a blind receipt order. Required when **Enabled** is on. Without it, saving the warehouse is refused: *Set a blind receipt pool on warehouse ...* |

Repeat for each warehouse that receives blind freight. A client that ships to several warehouses gets a separate order per warehouse, because the order's warehouse must match the worker's.

## 5. Change management

Blind receipt orders can't use purchase order change management. With approval workflow on, every line the device adds would send the order back for approval. Saving a blind receipt order with change management on is refused:

> Blind receipt orders can't use change management. Turn off change management on purchase order *PO number*.

If your company doesn't use change management, there is nothing to do. If it does:

1. In **Procurement and sourcing parameters**, turn on **Allow override of settings per vendor**.
2. For the vendor account of each blind receiving client, override the setting and turn change management off. Leads can also turn it off on each order.

If override per vendor isn't allowed, change management can't be turned off on an order, and blind receipt orders can't be saved.

## 6. Mobile device menu item

1. Go to **Warehouse management > Setup > Mobile device > Mobile device menu items** and create a menu item. Give it a name and title your workers recognize, for example *Blind PO receiving*.
2. Set the fields below and save.

| Field | Value |
|---|---|
| **Mode** | **Indirect** |
| **Work creation process** | **Mixed license plate receiving** |
| **Source document line identification method** | **Blind PO receiving** |

3. Go to **Warehouse management > Setup > Mobile device > Mobile device menu** and add the menu item to the menu your receiving workers use.

The menu item receives only. **LP Complete** creates put-away work for the license plate, and workers put the pallet away afterwards with the standard put-away flow.

## 7. Standard setup the flow reuses

Check these before going live:

| Setup | What the flow needs |
|---|---|
| **Put-away** | Blind receipts use the existing purchase order put-away work templates and location directives, unchanged. |
| **Receipt location** | Each warehouse needs a default receipt location, since no load or dock is assigned. |
| **License plate labels** | Labels print through document routing. Add a routing rule for the new menu item, or no label prints. |
| **Item identity (optional)** | To show the client's item number on the quantity screen, add the menu item to **Item identity menu item settings**. It resolves for the order's vendor. See the [item identity setup guide](ItemIdentitySetup.md). |

## 8. Nightly posting

Schedule the posting batch so registered receipts are posted without anyone posting by hand, and month end needs no posting step.

1. Go to **Warehouse management > Periodic tasks > Post blind receipts**.
2. Run it in batch with a nightly recurrence.
3. Repeat in each company that uses blind PO receiving.

Each run does three things:

1. **Deletes empty lines.** It deletes lines the device added that no longer have any registered or received quantity and were added more than 12 hours ago. Each line is deleted on its own, so a line that can't be deleted is logged and left for the next run. Lines still on a license plate that isn't complete are kept.
2. **Confirms orders.** It confirms each blind receipt order it's about to post, if the order isn't confirmed yet. Standard posts product receipts only for confirmed orders. Without change management, lines added later don't undo the confirmation.
3. **Posts product receipts.** It posts a product receipt for the registered quantity of each blind receipt order, one order at a time. Receipts are numbered after the order: `<PO number>-1` for the first night's posting, `<PO number>-2` for the next, and so on.

If an order fails, the other orders still post. The run logs *The product receipt for purchase order ... wasn't posted.* for that order, and ends with an error so the batch job shows as failed:

> Blind receipt posting: *X* orders posted, *Y* failed, *Z* unregistered lines deleted.

## 9. Corrections

| Situation | What happens |
|---|---|
| Put-away work of a blind receipt is cancelled | With **Unregister receipt when canceling work** turned on in **Warehouse management parameters**, cancelling the work unregisters the receipt and deletes the lines the device added. Turn the parameter on if workers or leads correct receipts by cancelling work. |
| A receipt is unregistered another way, for example from the registration form | The line stays on the order until the next nightly run deletes it. |
| The product receipt is already posted | Cancel or correct the product receipt first, as standard. |

## 10. Security

| Element | Grants |
|---|---|
| Duty **Post blind receipts** | The **Post blind receipts** batch job. It isn't on any role. Assign it to the role that schedules batch jobs, for example the warehouse manager. |
| Privilege **Post blind receipts** | Included in the duty; use it directly for finer-grained role design. |

- **Setup:** the warehouse settings are on the standard **Warehouses** form, so anyone who can maintain warehouses can set them up.
- **Leads** create blind receipt orders with their existing purchasing access.
- **Workers** need no new access.

## 11. Limitations

- Catch weight items aren't supported; the device refuses them with their own message.
- An order line is resized only while nothing on it is registered.

## 12. Check the setup

1. Turn **Enabled** on for the warehouse and clear **Blind receipt pool**. Saving the warehouse is refused: *Set a blind receipt pool on warehouse ...*
2. Set the pool and save the warehouse.
3. Create a test order for that warehouse, in its blind receipt pool, with no lines and change management off. Use a vendor that is the primary vendor of a test item.
4. On the mobile device, sign in as a worker of that warehouse and open the new menu item. Enter a new license plate, the order number, the test item and a quantity, and select **OK**. The order now has a zero-price line for the item.
5. Select **LP Complete**. The license plate gets one put-away work.
6. Run **Post blind receipts**. The order has product receipt `<PO number>-1`.
