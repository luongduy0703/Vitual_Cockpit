from setuptools import find_packages, setup
import os
from glob import glob

package_name = 'wicom_roboarm_ros2'

setup(
    name=package_name,
    version='1.0.0',
    packages=find_packages(exclude=['test']),
    data_files=[
        ('share/ament_index/resource_index/packages',
            ['resource/' + package_name]),
        ('share/' + package_name, ['package.xml']),
        (os.path.join('share', package_name, 'launch'), glob('launch/*.py')),
    ],
    install_requires=['setuptools'],
    zip_safe=True,
    maintainer='EmNetLab411',
    maintainer_email='emnetlab411@example.com',
    description='Robotic Arm Control for Virtual-Copilot-UAM (ROS2)',
    license='MIT',
    tests_require=['pytest'],
    entry_points={
        'console_scripts': [
            'roboarm_node = wicom_roboarm_ros2.roboarm_node:main',
        ],
    },
)
