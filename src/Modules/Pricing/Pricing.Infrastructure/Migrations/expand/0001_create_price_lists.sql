-- Price lists owned by the Pricing module (ADR-0007, ADR-0018): one base list that applies to every account, plus
-- customer-specific lists that override the base price per SKU. SKUs are the product ids of the external inventory
-- system; a SKU without a row in an applicable list cannot be ordered (unknown-product), which validates products
-- without calling that system in the request path.
-- account_id has no foreign key: customer data lives in the customers schema and no module reads another module's
-- schema (ADR-0007). The shipping charge belongs to the list, so a customer list can carry its own; ShippingChargeRule
-- applies it while the Pricing.ShippingCharge flag is on (ADR-0018, ADR-0019).

set lock_timeout = '5s';

begin;

create table if not exists pricing.price_lists (
    id                uuid          primary key,
    version           text          not null,
    account_id        uuid,
    shipping_charge   numeric(12, 2) not null,
    shipping_tax_rate numeric(5, 4)  not null
);

-- One list per account and exactly one base list; nulls not distinct makes the base list (account_id null) unique too.
create unique index if not exists ux_price_lists_account on pricing.price_lists (account_id) nulls not distinct;

create table if not exists pricing.price_list_items (
    price_list_id uuid           not null references pricing.price_lists (id),
    sku           text           not null,
    unit_price    numeric(12, 2) not null,
    tax_rate      numeric(5, 4)  not null,
    primary key (price_list_id, sku)
);

commit;
