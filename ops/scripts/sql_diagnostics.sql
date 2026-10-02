-- Run on the production replica/DB when "the app is slow". Read-only DMV queries.
-- 1) Top queries by total CPU
SELECT TOP 10 qs.total_worker_time/qs.execution_count AS avg_cpu_us, qs.execution_count, qs.total_logical_reads/qs.execution_count AS avg_reads,
       SUBSTRING(st.text, (qs.statement_start_offset/2)+1, 200) AS stmt, OBJECT_NAME(st.objectid) AS proc_name
FROM sys.dm_exec_query_stats qs CROSS APPLY sys.dm_exec_sql_text(qs.sql_handle) st ORDER BY qs.total_worker_time DESC;
-- 2) Blocking chains
SELECT r.session_id, r.blocking_session_id, r.wait_type, r.wait_time, DB_NAME(r.database_id) db, t.text
FROM sys.dm_exec_requests r CROSS APPLY sys.dm_exec_sql_text(r.sql_handle) t WHERE r.blocking_session_id <> 0;
-- 3) Missing indexes (validate before creating!)
SELECT TOP 10 d.statement, d.equality_columns, d.inequality_columns, d.included_columns, s.user_seeks, s.avg_total_user_cost*s.avg_user_impact AS score
FROM sys.dm_db_missing_index_details d JOIN sys.dm_db_missing_index_groups g ON g.index_handle=d.index_handle JOIN sys.dm_db_missing_index_group_stats s ON s.group_handle=g.index_group_handle ORDER BY score DESC;
-- 4) Outbox backlog (is the relay stuck?)
SELECT COUNT(*) pending, MIN(CreatedUtc) oldest FROM SupplyMesh.sm.Outbox WHERE PublishedUtc IS NULL;
-- 5) Deadlocks (system_health XE)
SELECT TOP 5 CAST(xet.target_data AS XML) FROM sys.dm_xe_session_targets xet JOIN sys.dm_xe_sessions xe ON xe.address=xet.event_session_address WHERE xe.name='system_health';
