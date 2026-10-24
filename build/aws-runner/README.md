# Temporary analyzer validation runner

Terraform creates one Ubuntu EC2 instance, a temporary public SSH key and a
security group permitting SSH only from the caller's `/32`. No AWS credentials
or application data go onto the runner. The AWS free account permits
`m7i-flex.large` (2 vCPU / 8 GiB). Run one build/test process at a time.

The instance schedules its own termination six hours after boot. Its encrypted
root disk is deleted on termination. This bounds compute lifetime; it does not
replace explicit Terraform cleanup. There is no NAT gateway or reserved instance.

Prerequisites: Terraform, authenticated AWS CLI, SSH, Python 3, and an existing
default VPC in us-east-1. `tf.py` obtains ephemeral credentials from AWS CLI in
memory, supporting AWS login without writing keys into configuration or state.

```sh
ssh-keygen -t ed25519 -N '' -f build/aws-runner/runner-key
# Create ignored local.auto.tfvars containing ssh_public_key and ssh_cidr.
terraform -chdir=build/aws-runner init
python3 build/aws-runner/tf.py plan -out=runner.tfplan
python3 build/aws-runner/tf.py apply runner.tfplan
python3 build/aws-runner/tf.py output
# Upload tracked source only (git archive), plus the reviewed candidate package.
# Never copy local application directories, tokens, NuGet credentials or .git.
# Retrieve logs and changed sources before cleanup.
python3 build/aws-runner/tf.py destroy -auto-approve
```

State, plans, SSH private keys and variable values are ignored. Keep state until
cleanup succeeds. Bootstrap installs pinned .NET 11 SDK and the .NET 10 runtime
host required by repository tooling. Use `cloud-init status --wait` remotely to
verify setup; inspect cloud-init-output.log if setup fails.
