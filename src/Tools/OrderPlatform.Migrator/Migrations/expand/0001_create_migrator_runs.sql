set lock_timeout = '5s';

create table if not exists platform.migrator_runs (
    id                  bigint generated always as identity primary key,
    application_version text        not null,
    completed_at        timestamptz not null default now()
);
