-- v1 seed: the two accounts of the local Keycloak realm (deploy/keycloak/orderplatform-realm.json), so a token issued
-- there resolves to a customer account. CRM/ERP synchronisation replaces this seed (README §2, backlog).
-- ACME is domestic (tax charged), Globex is a cross-border EU business (VAT reverse charge).

set lock_timeout = '5s';

begin;

insert into customers.accounts (
    id, name, status, tax_country_code, tax_vat_number, tax_reverse_charge,
    billing_address_id, shipping_address_id, primary_contact_id)
values
    ('0b5c0d8e-7a1f-4c3e-9d2a-6f4b8e1a2c01', 'ACME Industries B.V.', 'active', 'NL', 'NL123456789B01', false,
     '11111111-1111-4111-8111-111111111101', '11111111-1111-4111-8111-111111111102', '11111111-1111-4111-8111-11111111110c'),
    ('0b5c0d8e-7a1f-4c3e-9d2a-6f4b8e1a2c02', 'Globex Corporation SA', 'active', 'BE', 'BE0987654321', true,
     '22222222-2222-4222-8222-222222222201', '22222222-2222-4222-8222-222222222202', '22222222-2222-4222-8222-22222222220c')
on conflict (id) do nothing;

insert into customers.addresses (id, account_id, line1, postal_code, city, country_code)
values
    ('11111111-1111-4111-8111-111111111101', '0b5c0d8e-7a1f-4c3e-9d2a-6f4b8e1a2c01', 'Keizersgracht 1', '1015 CJ', 'Amsterdam', 'NL'),
    ('11111111-1111-4111-8111-111111111102', '0b5c0d8e-7a1f-4c3e-9d2a-6f4b8e1a2c01', 'Havenweg 12', '3011 BN', 'Rotterdam', 'NL'),
    ('22222222-2222-4222-8222-222222222201', '0b5c0d8e-7a1f-4c3e-9d2a-6f4b8e1a2c02', 'Rue de la Loi 20', '1000', 'Brussels', 'BE'),
    ('22222222-2222-4222-8222-222222222202', '0b5c0d8e-7a1f-4c3e-9d2a-6f4b8e1a2c02', 'Industrielaan 5', '2000', 'Antwerp', 'BE')
on conflict (id) do nothing;

insert into customers.contacts (id, account_id, full_name, email, phone)
values
    ('11111111-1111-4111-8111-11111111110c', '0b5c0d8e-7a1f-4c3e-9d2a-6f4b8e1a2c01', 'Pat Buyer', 'portal.user@acme.example', '+31 20 555 0101'),
    ('22222222-2222-4222-8222-22222222220c', '0b5c0d8e-7a1f-4c3e-9d2a-6f4b8e1a2c02', 'Robin Procure', 'robin.procure@globex.example', '+32 2 555 0202')
on conflict (id) do nothing;

commit;
