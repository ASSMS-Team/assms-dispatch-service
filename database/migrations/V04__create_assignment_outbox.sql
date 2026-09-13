-- US-06B: an assignment and its outbound JobAssigned event are written in one
-- Dispatch-owned transaction. Kafka publication happens later from this outbox.
CREATE TABLE IF NOT EXISTS assignment_outbox (
    id                CHAR(36)      NOT NULL,
    assignment_id     CHAR(36)      NOT NULL,
    event_type        VARCHAR(50)   NOT NULL,
    topic             VARCHAR(100)  NOT NULL,
    payload           JSON          NOT NULL,
    publish_attempts  INT           NOT NULL DEFAULT 0,
    last_error        VARCHAR(1000) NULL,
    created_at        DATETIME(6)   NOT NULL DEFAULT CURRENT_TIMESTAMP(6),
    published_at      DATETIME(6)   NULL,

    PRIMARY KEY (id),
    UNIQUE KEY uq_assignment_outbox_assignment (assignment_id),
    KEY ix_assignment_outbox_pending (published_at, created_at),
    CONSTRAINT fk_assignment_outbox_assignment
        FOREIGN KEY (assignment_id) REFERENCES technician_assignments(id)
);
