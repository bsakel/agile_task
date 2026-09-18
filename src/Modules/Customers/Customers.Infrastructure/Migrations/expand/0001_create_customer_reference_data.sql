-- Local read copy of the customer reference data owned by the external CRM/ERP (README §2, ADR-0007).
-- Personal data (contact names, e-mail addresses, postal addresses) lives only here (ADR-0016); other modules
-- reference the rows by id. The account's default billing/shipping address and primary contact are plain ids: a
-- foreign key in both directions would be circular, and the CRM/ERP stays the system of record.

set lock_timeout = '5s';

begin;

create table if not exists customers.accounts (
    id                  uuid primary key,
    name                text    not null,
    status              text    not null,
    tax_country_code    text    not null,
    tax_vat_number      text,
    tax_reverse_charge  boolean not null,
    billing_address_id  uuid    not null,
    shipping_address_id uuid    not null,
    primary_contact_id  uuid    not null,
    constraint ck_accounts_status check (status in ('active', 'suspended', 'closed'))
);

create table if not exists customers.addresses (
    id           uuid primary key,
    account_id   uuid not null references customers.accounts (id),
    line1        text not null,
    postal_code  text not null,
    city         text not null,
    country_code text not null
);

create table if not exists customers.contacts (
    id         uuid primary key,
    account_id uuid not null references customers.accounts (id),
    full_name  text not null,
    email      text not null,
    phone      text
);

create index if not exists ix_addresses_account on customers.addresses (account_id);

create index if not exists ix_contacts_account on customers.contacts (account_id);

commit;
