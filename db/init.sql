-- 演示用初始化脚本：创建 demo 库与示例数据
CREATE DATABASE IF NOT EXISTS demo DEFAULT CHARACTER SET utf8mb4 COLLATE utf8mb4_unicode_ci;

USE demo;

CREATE TABLE IF NOT EXISTS customers (
  id          INT AUTO_INCREMENT PRIMARY KEY,
  name        VARCHAR(50)  NOT NULL COMMENT '客户名称',
  city        VARCHAR(50)  NULL COMMENT '所在城市',
  balance     DECIMAL(12,2) NOT NULL DEFAULT 0.00 COMMENT '账户余额',
  vip         TINYINT(1)   NOT NULL DEFAULT 0 COMMENT '是否 VIP',
  created_at  DATETIME     NOT NULL DEFAULT CURRENT_TIMESTAMP COMMENT '创建时间',
  updated_at  DATETIME     NULL COMMENT '更新时间',
  KEY idx_city (city),
  KEY idx_created_at (created_at)
) ENGINE=InnoDB COMMENT='客户表';

CREATE TABLE IF NOT EXISTS orders (
  id           BIGINT AUTO_INCREMENT PRIMARY KEY,
  customer_id  INT NOT NULL COMMENT '客户 ID',
  amount       DECIMAL(12,2) NOT NULL COMMENT '订单金额',
  status       ENUM('pending','paid','shipped','cancelled') NOT NULL DEFAULT 'pending',
  remark       TEXT NULL,
  created_at   DATETIME NOT NULL DEFAULT CURRENT_TIMESTAMP,
  KEY idx_customer (customer_id),
  CONSTRAINT fk_orders_customer FOREIGN KEY (customer_id) REFERENCES customers(id)
) ENGINE=InnoDB COMMENT='订单表';

INSERT INTO customers (name, city, balance, vip)
VALUES
  ('张伟', '北京', 12800.50, 1),
  ('李娜', '上海', 3200.00, 0),
  ('王芳', '广州', 8750.75, 1),
  ('刘洋', '成都', 640.20, 0),
  ('陈静', '杭州', 15320.00, 1);

INSERT INTO orders (customer_id, amount, status, remark)
VALUES
  (1, 1999.00, 'paid', '首批订单'),
  (1, 329.50, 'shipped', NULL),
  (2, 899.00, 'pending', '等待付款'),
  (3, 4599.00, 'paid', '大客户'),
  (4, 129.90, 'cancelled', '用户取消'),
  (5, 7800.00, 'paid', NULL);
