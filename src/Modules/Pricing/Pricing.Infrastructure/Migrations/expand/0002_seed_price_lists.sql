-- v1 seed: the base price list plus one customer-specific list for ACME, the account seeded by the customers schema
-- (0b5c0d8e-...c01). A price list import API or ERP synchronisation replaces this seed (README §2, backlog).
-- ACME gets a lower price for SKU-1000 and its own shipping charge; Globex has no list of its own, so the base list
-- applies to it. SKU-2000 carries the reduced VAT rate, so an order can exercise more than one tax rate.

set lock_timeout = '5s';

begin;

insert into pricing.price_lists (id, version, account_id, shipping_charge, shipping_tax_rate)
values
    ('7c1d3a90-5e62-4a18-9b74-2f0c6d8e1a00', 'base-2026-09', null, 12.50, 0.2100),
    ('7c1d3a90-5e62-4a18-9b74-2f0c6d8e1a01', 'acme-2026-09', '0b5c0d8e-7a1f-4c3e-9d2a-6f4b8e1a2c01', 9.95, 0.2100)
on conflict (id) do nothing;

insert into pricing.price_list_items (price_list_id, sku, unit_price, tax_rate)
values
    ('7c1d3a90-5e62-4a18-9b74-2f0c6d8e1a00', 'SKU-1000', 10.00, 0.2100),
    ('7c1d3a90-5e62-4a18-9b74-2f0c6d8e1a00', 'SKU-1001', 24.95, 0.2100),
    ('7c1d3a90-5e62-4a18-9b74-2f0c6d8e1a00', 'SKU-2000', 4.95, 0.0900),
    ('7c1d3a90-5e62-4a18-9b74-2f0c6d8e1a01', 'SKU-1000', 8.50, 0.2100)
on conflict (price_list_id, sku) do nothing;

commit;
