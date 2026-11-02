CREATE SCHEMA IF NOT EXISTS ledger;
CREATE SCHEMA IF NOT EXISTS balance;
CREATE TABLE ledger.accounts (
 "Id" uuid PRIMARY KEY, "CustomerId" varchar(200) NOT NULL, "OpenedAt" timestamptz NOT NULL,
 "Balance" numeric(19,4) NOT NULL DEFAULT 0 CHECK ("Balance">=0), "Sequence" bigint NOT NULL DEFAULT 0);
CREATE INDEX ON ledger.accounts ("CustomerId");
CREATE TABLE ledger.entries (
 "Id" uuid PRIMARY KEY, "AccountId" uuid NOT NULL REFERENCES ledger.accounts("Id"), "Sequence" bigint NOT NULL,
 "Type" integer NOT NULL CHECK ("Type" IN (1,2)), "Amount" numeric(19,4) NOT NULL CHECK ("Amount">0),
 "Description" text NOT NULL, "ExternalReference" text, "RegisteredAt" timestamptz NOT NULL,
 "ReversalOf" uuid UNIQUE REFERENCES ledger.entries("Id"), UNIQUE("AccountId","Sequence"));
CREATE INDEX ON ledger.entries ("AccountId","RegisteredAt","Sequence");
CREATE FUNCTION ledger.protect_entry() RETURNS trigger LANGUAGE plpgsql AS $$ BEGIN RAISE EXCEPTION 'Ledger entries are immutable' USING ERRCODE='23514'; END $$;
CREATE TRIGGER immutable_entry BEFORE UPDATE OR DELETE ON ledger.entries FOR EACH ROW EXECUTE FUNCTION ledger.protect_entry();
CREATE FUNCTION ledger.validate_reversal() RETURNS trigger LANGUAGE plpgsql AS $$
DECLARE original ledger.entries; BEGIN
 IF NEW."ReversalOf" IS NOT NULL THEN
 SELECT * INTO original FROM ledger.entries WHERE "Id"=NEW."ReversalOf";
 IF NOT FOUND OR original."AccountId"<>NEW."AccountId" OR original."ReversalOf" IS NOT NULL
 OR original."Amount"<>NEW."Amount" OR original."Type"=NEW."Type" THEN
 RAISE EXCEPTION 'Invalid reversal' USING ERRCODE='23514'; END IF; END IF; RETURN NEW; END $$;
CREATE TRIGGER valid_reversal BEFORE INSERT ON ledger.entries FOR EACH ROW EXECUTE FUNCTION ledger.validate_reversal();
CREATE TABLE ledger.idempotency_keys (
 scope uuid NOT NULL, key varchar(128) NOT NULL, request_hash char(64) NOT NULL,
 status varchar(16) NOT NULL CHECK(status IN ('InProgress','Completed')), status_code integer, response_body text,
 created_at timestamptz NOT NULL DEFAULT clock_timestamp(), expires_at timestamptz NOT NULL,
 PRIMARY KEY(scope,key));
CREATE INDEX ON ledger.idempotency_keys(expires_at);
CREATE TABLE ledger.outbox_messages (
 id uuid PRIMARY KEY, payload jsonb NOT NULL, created_at timestamptz NOT NULL, published_at timestamptz,
 attempts integer NOT NULL DEFAULT 0, next_attempt_at timestamptz NOT NULL DEFAULT clock_timestamp(), dead_lettered_at timestamptz);
CREATE INDEX ON ledger.outbox_messages(next_attempt_at,created_at) WHERE published_at IS NULL AND dead_lettered_at IS NULL;
CREATE TABLE balance.account_balances (
 account_id uuid PRIMARY KEY, customer_id varchar(200) NOT NULL, opened_at timestamptz NOT NULL,
 amount numeric(19,4) NOT NULL DEFAULT 0 CHECK(amount>=0), last_sequence bigint NOT NULL DEFAULT 0, processed_at timestamptz NOT NULL);
CREATE INDEX ON balance.account_balances(customer_id);
CREATE TABLE balance.statement_entries (
 message_id uuid PRIMARY KEY, account_id uuid NOT NULL REFERENCES balance.account_balances(account_id), sequence bigint NOT NULL,
 type integer NOT NULL CHECK(type IN(1,2)), amount numeric(19,4) NOT NULL CHECK(amount>0), registered_at timestamptz NOT NULL, reversal_of uuid,
 UNIQUE(account_id,sequence));
CREATE INDEX ON balance.statement_entries(account_id,registered_at,sequence);
CREATE TABLE balance.processed_messages(message_id uuid PRIMARY KEY,processed_at timestamptz NOT NULL);
CREATE TABLE balance.balance_snapshots (
 account_id uuid NOT NULL REFERENCES balance.account_balances(account_id), sequence bigint NOT NULL,
 registered_at timestamptz NOT NULL, amount numeric(19,4) NOT NULL, PRIMARY KEY(account_id,sequence));
