# AAX Warehouse Tools — Blind PO Receiving User Guide

This guide is for the people who receive freight that arrives with no purchase order or advance notice: the **lead** who opens the order, and the **warehouse workers** who receive it on the mobile app. For first-time setup, start with the [setup guide](BlindReceivingSetup.md).

## 1. How it works

Vendors ship and drop off goods without notice, so orders can't be opened ahead of time. Instead:

1. **The lead** creates one empty purchase order per arrival, in the warehouse's blind receipt pool, and passes its number to the worker with the paperwork.
2. **The worker** scans a license plate and the order number on the mobile device, then identifies each item as it comes off the truck.
3. **Each confirmed item** adds a zero-price line to the order and puts the quantity on the license plate.
4. **LP Complete** registers the plate, creates its put-away work and prints its label.
5. **A nightly batch** posts the product receipt for what was registered. Workers can still add lines to the same order the next day.

Freight from a sender nobody can identify waits in the physical hold area until the lead knows whose it is.

## 2. For the lead: create a blind receipt order

When a shipment arrives:

1. Go to **Procurement and sourcing > Purchase orders > All purchase orders** and create a purchase order.
2. Set the **vendor account** to the client the goods belong to.
3. Set the **site and warehouse** to the warehouse that receives the goods.
4. Set the order's **purchase pool** to that warehouse's blind receipt pool. Ask your administrator which pool it is.
5. Add **no lines**. Change management must be off on the order, or it can't be saved.
6. Save the order and pass its number to the worker with the paperwork.

One order covers one arrival at one warehouse. If a client's goods arrive at two warehouses, create an order for each.

## 3. For the worker: receive on the device

Open the **Blind PO receiving** menu item. Your administrator may have given it another name.

1. **License plate.** Scan or enter the pallet's license plate. Use a new one, or an open plate you are still building.
2. **PO number.** Scan or key the number the lead gave you. The device checks the order before you receive anything (see section 6, Messages). An order whose earlier lines were already posted overnight is still accepted.
3. **Item.** Scan the item. Its primary vendor must be the order's vendor. Any other item is refused.
4. **Dimensions, quantity and unit.** If the item has product dimensions, such as size or color, enter them. Then enter the quantity and unit. The unit is the purchase unit by default. Batch, serial number or expiry date screens appear when the item's tracking requires them.
5. **OK.** The device adds a line to the order at zero price, with no discount or charges. It records you and the time on the line, and puts the quantity on the license plate.

Repeat steps 3 to 5 for each item on the pallet. Each confirmation adds its own line, even for the same item.

6. **Show list.** Lists the lines on the plate. To fix a quantity, select the line and choose **Modify**. The order line is resized to match, in the item's inventory unit. Quantity 0 removes the line from the plate. The nightly run deletes its empty order line.
7. **LP Complete.** Registers everything on the plate, creates one put-away work for the plate and prints its label. Put the pallet away with your usual put-away menu item.

A pallet holds items of one order only. Start a new license plate for the next order.

## 4. After receiving

| Topic | What happens |
|---|---|
| **Put-away** | The put-away work from **LP Complete** is done with the standard put-away flow. |
| **Posting** | A nightly batch posts a product receipt for the registered quantity of each order. Receipts are numbered after the order: `<PO number>-1` for the first night, `<PO number>-2` for the next. Nobody needs to post at month end. |
| **Adding more later** | Workers can add lines to the same order on a later day. The next nightly run posts them as a new product receipt. |

## 5. Corrections

| Situation | What to do |
|---|---|
| Wrong quantity or item, plate not complete yet | Use **Show list** and **Modify**. Set the quantity to 0 to take an item off the plate. |
| Plate completed, product receipt not posted yet | Cancel the plate's put-away work. If your administrator turned on **Unregister receipt when canceling work**, this unregisters the receipt and deletes the lines the device added. Then receive the pallet again. |
| Receipt unregistered another way, for example from the registration form | Nothing more to do. The empty line stays on the order until the next nightly run deletes it. |
| Product receipt already posted | Cancel or correct the product receipt first, as standard. |

**Audit.** Each line the device added records the mobile worker who added it, in **Blind receipt worker**, and when, in **Blind receipt added**. Both show on the purchase order line under **Line details > General > Blind receipt**. The purchase line's standard *created by* shows the shared mobile account.

## 6. Messages

The device checks the order number, the license plate and each item before anything is received. Each refusal names the problem:

| Message | Cause | What to do |
|---|---|---|
| *Blind PO receiving isn't enabled for warehouse ...* | Your warehouse doesn't use blind PO receiving. | Ask your administrator to turn on **Enabled** for the warehouse. |
| *Set a blind receipt pool on warehouse ...* | The warehouse has the feature on but no pool. | Ask your administrator to set the warehouse's **Blind receipt pool**. |
| *The purchase order number is not valid.* | The number was mistyped, or the order wasn't created. | Check the paperwork, then ask the lead. |
| *Purchase order ... has no warehouse. Ask the lead.* | The order has no warehouse set. | The lead sets the order's site and warehouse. |
| *Purchase order ... is for warehouse ..., not warehouse ... Ask the lead.* | The order is for another warehouse. | The lead creates an order for your warehouse. |
| *Purchase order ... isn't a blind receipt order. Ask the lead.* | The order isn't in your warehouse's blind receipt pool, or isn't a regular purchase order. | The lead creates the order in the right pool. |
| *The purchase order is not open and cannot be received.* | The order was cancelled or closed. | The lead creates a new order. |
| *License plate ... already holds items of purchase order .... Complete it or use another license plate.* | The plate is being built for another order. | Complete that plate, or start a new license plate for this order. |
| *Item ... doesn't belong to vendor ..., the vendor on this order. Set it aside and ask the lead.* | The item's primary vendor isn't the order's vendor. | Set the item aside. The lead finds out whose it is. |
| *Item ... is a catch weight item. Blind PO receiving can't receive it. Ask the lead.* | Catch weight items can't be received with this flow. | The lead receives the item another way. |

Leads can see one more message, when saving an order:

| Message | Cause | What to do |
|---|---|---|
| *Blind receipt orders can't use change management. Turn off change management on purchase order ...* | The order is in a blind receipt pool and has change management on. | Turn change management off on the order. If you can't, ask your administrator (see *Change management* in the [setup guide](BlindReceivingSetup.md)). |

## 7. Good to know

- Catch weight items can't be received with this flow; the device refuses them.
- **Modify** resizes the order line only while nothing on it is registered. After **LP Complete**, correct the receipt as described in section 5.
