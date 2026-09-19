-- Dispatch owns Technician records. These records are intentionally separate
-- from Customer & Asset StaffAccounts and have no cross-service foreign key.
CREATE TABLE IF NOT EXISTS technicians (
    id                   CHAR(36)     NOT NULL,
    technician_reference VARCHAR(30)  NOT NULL,
    full_name            VARCHAR(150) NOT NULL,
    region               VARCHAR(20)  NOT NULL,
    status               VARCHAR(10)  NOT NULL DEFAULT 'ACTIVE',
    phone                VARCHAR(30)  NULL,
    email                VARCHAR(254) NULL,
    created_at           TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP,
    updated_at           TIMESTAMP    NOT NULL DEFAULT CURRENT_TIMESTAMP ON UPDATE CURRENT_TIMESTAMP,

    PRIMARY KEY (id),
    UNIQUE KEY uq_technicians_reference (technician_reference),
    KEY idx_technicians_region_status (region, status),

    CONSTRAINT chk_technicians_region CHECK (region IN ('WESTERN', 'CENTRAL', 'SOUTHERN', 'NORTHERN', 'EASTERN', 'NORTH_WESTERN', 'NORTH_CENTRAL', 'UVA', 'SABARAGAMUWA')),
    CONSTRAINT chk_technicians_status CHECK (status IN ('ACTIVE', 'INACTIVE'))
);

CREATE TABLE IF NOT EXISTS technician_skills (
    technician_id CHAR(36)    NOT NULL,
    skill         VARCHAR(50) NOT NULL,

    PRIMARY KEY (technician_id, skill),
    CONSTRAINT fk_technician_skills_technician
        FOREIGN KEY (technician_id) REFERENCES technicians(id)
        ON DELETE CASCADE
);
