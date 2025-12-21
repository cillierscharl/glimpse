#!/bin/bash
# Install NVIDIA Container Toolkit for Docker GPU support

set -e

echo "Installing NVIDIA Container Toolkit..."

# Add NVIDIA repo key
curl -fsSL https://nvidia.github.io/libnvidia-container/gpgkey | sudo gpg --dearmor -o /usr/share/keyrings/nvidia-container-toolkit-keyring.gpg

# Add repo
curl -s -L https://nvidia.github.io/libnvidia-container/stable/deb/nvidia-container-toolkit.list | \
  sed 's#deb https://#deb [signed-by=/usr/share/keyrings/nvidia-container-toolkit-keyring.gpg] https://#g' | \
  sudo tee /etc/apt/sources.list.d/nvidia-container-toolkit.list

# Install
sudo apt-get update
sudo apt-get install -y nvidia-container-toolkit

# Configure Docker runtime
sudo nvidia-ctk runtime configure --runtime=docker
sudo systemctl restart docker

echo ""
echo "NVIDIA Container Toolkit installed!"
echo ""
echo "Now uncomment the GPU section in docker-compose.yml and run:"
echo "  docker compose up -d"
