#!/bin/bash

# CarePro Backend Docker Build and Deploy Script
# This script helps build and deploy the CarePro backend using Docker

set -e  # Exit on any error

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
BLUE='\033[0;34m'
NC='\033[0m' # No Color

# Configuration
IMAGE_NAME="carepro-cnet-backend"
IMAGE_TAG="${1:-latest}"
CONTAINER_NAME="carepro-api"
DOCKER_REGISTRY="${DOCKER_REGISTRY:-}"

# Functions
print_info() {
    echo -e "${BLUE}[INFO]${NC} $1"
}

print_success() {
    echo -e "${GREEN}[SUCCESS]${NC} $1"
}

print_warning() {
    echo -e "${YELLOW}[WARNING]${NC} $1"
}

print_error() {
    echo -e "${RED}[ERROR]${NC} $1"
}

check_requirements() {
    print_info "Checking requirements..."
    
    if ! command -v docker &> /dev/null; then
        print_error "Docker is not installed or not in PATH"
        exit 1
    fi
    
    if ! command -v docker-compose &> /dev/null; then
        print_error "Docker Compose is not installed or not in PATH"
        exit 1
    fi
    
    print_success "All requirements satisfied"
}

build_image() {
    print_info "Building Docker image: ${IMAGE_NAME}:${IMAGE_TAG}"
    
    docker build \
        --tag "${IMAGE_NAME}:${IMAGE_TAG}" \
        --tag "${IMAGE_NAME}:latest" \
        --build-arg BUILD_DATE="$(date -u +'%Y-%m-%dT%H:%M:%SZ')" \
        --build-arg VCS_REF="$(git rev-parse --short HEAD)" \
        .
    
    print_success "Image built successfully"
}

run_tests() {
    print_info "Running tests in container..."
    
    docker run --rm \
        --name "${CONTAINER_NAME}-test" \
        "${IMAGE_NAME}:${IMAGE_TAG}" \
        dotnet test --logger "console;verbosity=minimal"
    
    print_success "Tests passed"
}

run_dev() {
    print_info "Starting development environment..."
    
    # Copy environment file if it doesn't exist
    if [ ! -f .env ]; then
        print_warning ".env file not found, copying from .env.example"
        cp .env.example .env
        print_warning "Please update .env file with your actual values"
    fi
    
    docker-compose up --build -d
    
    print_success "Development environment started"
    print_info "API available at: http://localhost:5000"
    print_info "Swagger UI: http://localhost:5000/swagger"
    print_info "MongoDB: localhost:27017"
    
    # Show logs
    docker-compose logs -f carepro-api
}

run_prod() {
    print_info "Starting production environment..."
    
    if [ ! -f .env ]; then
        print_error ".env file is required for production deployment"
        exit 1
    fi
    
    docker-compose -f docker-compose.prod.yml up -d
    
    print_success "Production environment started"
}

stop_services() {
    print_info "Stopping services..."
    
    docker-compose down
    docker-compose -f docker-compose.prod.yml down 2>/dev/null || true
    
    print_success "Services stopped"
}

push_image() {
    if [ -z "$DOCKER_REGISTRY" ]; then
        print_error "DOCKER_REGISTRY environment variable is required for pushing"
        exit 1
    fi
    
    print_info "Pushing image to registry: ${DOCKER_REGISTRY}"
    
    # Tag for registry
    docker tag "${IMAGE_NAME}:${IMAGE_TAG}" "${DOCKER_REGISTRY}/${IMAGE_NAME}:${IMAGE_TAG}"
    docker tag "${IMAGE_NAME}:${IMAGE_TAG}" "${DOCKER_REGISTRY}/${IMAGE_NAME}:latest"
    
    # Push to registry
    docker push "${DOCKER_REGISTRY}/${IMAGE_NAME}:${IMAGE_TAG}"
    docker push "${DOCKER_REGISTRY}/${IMAGE_NAME}:latest"
    
    print_success "Image pushed successfully"
}

show_help() {
    cat << EOF
CarePro Backend Docker Management Script

Usage: $0 [COMMAND] [TAG]

Commands:
    build       Build Docker image
    test        Run tests in container
    dev         Start development environment
    prod        Start production environment
    stop        Stop all services
    push        Push image to registry
    logs        Show container logs
    shell       Open shell in running container
    clean       Clean up unused Docker resources
    help        Show this help message

Examples:
    $0 build                    # Build with 'latest' tag
    $0 build v1.0.0            # Build with specific tag
    $0 dev                     # Start development environment
    $0 prod                    # Start production environment
    $0 push v1.0.0             # Push specific version to registry

Environment Variables:
    DOCKER_REGISTRY            # Docker registry URL for push command

EOF
}

logs() {
    print_info "Showing container logs..."
    docker-compose logs -f
}

shell() {
    print_info "Opening shell in running container..."
    docker exec -it "${CONTAINER_NAME}" /bin/bash
}

clean() {
    print_info "Cleaning up unused Docker resources..."
    
    docker system prune -f
    docker volume prune -f
    
    print_success "Cleanup completed"
}

# Main script logic
case "${1:-help}" in
    build)
        check_requirements
        build_image
        ;;
    test)
        check_requirements
        run_tests
        ;;
    dev)
        check_requirements
        run_dev
        ;;
    prod)
        check_requirements
        run_prod
        ;;
    stop)
        stop_services
        ;;
    push)
        check_requirements
        push_image
        ;;
    logs)
        logs
        ;;
    shell)
        shell
        ;;
    clean)
        clean
        ;;
    help|--help|-h)
        show_help
        ;;
    *)
        print_error "Unknown command: $1"
        show_help
        exit 1
        ;;
esac