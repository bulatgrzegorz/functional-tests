-- Runs once when the postgres container initializes (psql, so \connect works).
-- Aspire's own CREATE DATABASE for "ordersdb" then hits 42P04 and ignores it.
CREATE DATABASE ordersdb;
\connect ordersdb
CREATE TABLE IF NOT EXISTS orders (sku VARCHAR(50), quantity INT);
