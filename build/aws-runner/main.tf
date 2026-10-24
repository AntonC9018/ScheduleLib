terraform {
  required_version = ">= 1.6"
  required_providers {
    aws = { source = "hashicorp/aws", version = "~> 6.0" }
  }
}
provider "aws" { region = "us-east-1" }
variable "ssh_public_key" { type = string }
variable "ssh_cidr" { type = string }
data "aws_vpc" "default" { default = true }
data "aws_subnets" "default" {
  filter {
    name   = "vpc-id"
    values = [data.aws_vpc.default.id]
  }
}
data "aws_ami" "ubuntu" {
  most_recent = true
  owners      = ["099720109477"]
  filter {
    name   = "name"
    values = ["ubuntu/images/hvm-ssd-gp3/ubuntu-noble-24.04-amd64-server-*"]
  }
}
resource "aws_key_pair" "runner" {
  key_name_prefix = "schedulelib-review-"
  public_key      = var.ssh_public_key
}
resource "aws_security_group" "runner" {
  name_prefix = "schedulelib-review-"
  vpc_id      = data.aws_vpc.default.id
  ingress {
    from_port   = 22
    to_port     = 22
    protocol    = "tcp"
    cidr_blocks = [var.ssh_cidr]
  }
  egress {
    from_port   = 0
    to_port     = 0
    protocol    = "-1"
    cidr_blocks = ["0.0.0.0/0"]
  }
  tags = { Purpose = "schedulelib-warning-review" }
}
resource "aws_instance" "runner" {
  ami                                  = data.aws_ami.ubuntu.id
  instance_type                        = "m7i-flex.large"
  subnet_id                            = sort(data.aws_subnets.default.ids)[0]
  associate_public_ip_address          = true
  key_name                             = aws_key_pair.runner.key_name
  vpc_security_group_ids               = [aws_security_group.runner.id]
  instance_initiated_shutdown_behavior = "terminate"
  metadata_options { http_tokens = "required" }
  root_block_device {
    volume_size           = 30
    volume_type           = "gp3"
    encrypted             = true
    delete_on_termination = true
  }
  user_data = join("\n", [file("${path.module}/watchdog.sh"), file("${path.module}/bootstrap.sh")])
  tags      = { Name = "schedulelib-warning-review", Purpose = "schedulelib-warning-review" }
}
output "public_ip" { value = aws_instance.runner.public_ip }
output "instance_id" { value = aws_instance.runner.id }
